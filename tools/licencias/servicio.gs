/**
 * Servicio de licencias de los add-ins CQEING (issue #189).
 * Plan: docs/planes/2026-10-28-licencias-hoja-google.md. Cómo montarlo: tools/licencias/LEEME.md.
 *
 * Va pegado a la hoja «Licencias CQEING» y se publica como aplicación web. El gestor lo llama con un GET:
 *   <url>/exec?codigo=XXXX-XXXX-XXXX-XXXX&nombre=…&equipo=…&usuarioWindows=…&version=…
 * Responde {"estado":"pendiente"}, {"estado":"revocado"} o {"estado":"aprobado","licencia":{"datos":"…","firma":"…"}}.
 *
 * Lo firmado es un texto (Decisión 3 del plan), en UTF-8:
 *   cqeing-licencia-v1|<código>|<nombre>|<emitida ISO-8601 UTC>|<vence ISO-8601 UTC>
 * La firma es RSA PKCS#1 v1.5 con SHA-256, en base64. La clave privada vive en la propiedad del script CLAVE_PRIVADA.
 */

const PREFIJO = 'cqeing-licencia-v1';
const CODIGO_VALIDO = /^[0-9A-F]{4}(-[0-9A-F]{4}){3}$/;
const MAX_PENDIENTES = 50;
const MAX_TEXTO = 60;
const DIAS_POR_DEFECTO = 3;
const DIAS_MAXIMO = 30;
const ESTADOS = ['pendiente', 'aprobado', 'revocado'];

const HOJA_EQUIPOS = 'Equipos';
const HOJA_REGISTRO = 'Registro';
const HOJA_CONFIG = 'Config';

const COLUMNAS_EQUIPOS = ['Código', 'Nombre', 'Equipo', 'Usuario Windows', 'Estado', 'Solicitada', 'Última conexión', 'Versión gestor', 'Notas'];
const COLUMNAS_REGISTRO = ['Fecha', 'Código', 'Nombre', 'Equipo', 'Usuario Windows', 'Respuesta'];
/** Número de columna (desde 1) en la pestaña Equipos. */
const COL = { codigo: 1, nombre: 2, estado: 5, ultimaConexion: 7, version: 8 };

/** Se ejecuta una vez a mano desde el editor: crea las pestañas, los encabezados y la lista de estados. */
function configurar() {
  const libro = SpreadsheetApp.getActive();

  const equipos = obtenerHoja_(libro, HOJA_EQUIPOS, COLUMNAS_EQUIPOS);
  // El código como texto: que Sheets nunca lo convierta en fecha o número.
  equipos.getRange('A:A').setNumberFormat('@');
  const regla = SpreadsheetApp.newDataValidation().requireValueInList(ESTADOS, true).setAllowInvalid(false).build();
  equipos.getRange(2, COL.estado, equipos.getMaxRows() - 1, 1).setDataValidation(regla);

  obtenerHoja_(libro, HOJA_REGISTRO, COLUMNAS_REGISTRO);

  const config = libro.getSheetByName(HOJA_CONFIG) || libro.insertSheet(HOJA_CONFIG);
  if (!config.getRange('A1').getValue()) {
    config.getRange('A1:B2').setValues([['Días de vigencia', DIAS_POR_DEFECTO], ['Correo de aviso', '']]);
    config.getRange('C2').setValue('Vacío = el correo de la cuenta que implementó el script.');
  }

  // La pestaña vacía con la que nace toda hoja nueva.
  libro.getSheets()
    .filter(h => ![HOJA_EQUIPOS, HOJA_REGISTRO, HOJA_CONFIG].includes(h.getName()) && h.getLastRow() === 0)
    .forEach(h => libro.deleteSheet(h));
}

function doGet(e) {
  const p = (e && e.parameter) || {};
  const codigo = String(p.codigo || '').trim().toUpperCase();
  if (!CODIGO_VALIDO.test(codigo)) return responder_({ error: 'codigo' });

  const solicitud = {
    codigo,
    nombre: limpiar_(p.nombre),
    equipo: limpiar_(p.equipo),
    usuarioWindows: limpiar_(p.usuarioWindows),
    version: limpiar_(p.version),
  };

  // Dos pedidos a la vez del mismo equipo no deben crear dos filas.
  const candado = LockService.getScriptLock();
  try {
    candado.waitLock(10000);
    return responder_(atender_(solicitud, new Date()));
  } catch (error) {
    console.error(error);
    return responder_({ error: 'servidor' });
  } finally {
    candado.releaseLock();
  }
}

function atender_(solicitud, ahora) {
  const libro = SpreadsheetApp.getActive();
  const equipos = libro.getSheetByName(HOJA_EQUIPOS);
  const fila = buscarFila_(equipos, solicitud.codigo);

  let respuesta;
  if (fila === 0) {
    if (contarPendientes_(equipos) >= MAX_PENDIENTES) {
      // Alguien está llenando la hoja: no se agregan más filas hasta que se revisen las pendientes.
      anotar_(libro, ahora, solicitud, 'pendiente (límite de solicitudes, no se agregó)');
      return { estado: 'pendiente' };
    }
    equipos.appendRow([solicitud.codigo, solicitud.nombre, solicitud.equipo, solicitud.usuarioWindows,
      'pendiente', ahora, ahora, solicitud.version, '']);
    avisarSolicitud_(libro, solicitud);
    respuesta = { estado: 'pendiente' };
  } else {
    equipos.getRange(fila, COL.ultimaConexion).setValue(ahora);
    equipos.getRange(fila, COL.version).setValue(solicitud.version);
    const estado = String(equipos.getRange(fila, COL.estado).getValue()).trim().toLowerCase();
    if (estado === 'aprobado') {
      // Se firma el nombre de la hoja, no el que mandó el equipo: quien aprueba puede corregirlo.
      const nombre = limpiar_(equipos.getRange(fila, COL.nombre).getValue()) || solicitud.nombre;
      respuesta = { estado: 'aprobado', licencia: firmarLicencia_(solicitud.codigo, nombre, ahora, diasVigencia_(libro)) };
    } else if (estado === 'revocado') {
      respuesta = { estado: 'revocado' };
    } else {
      // Al abrirse, el gestor consulta con el usuario de Windows antes de que la persona escriba su nombre: mientras
      // nadie la haya aprobado, la fila toma el último nombre que llegó. Aprobada, el nombre ya no se toca.
      if (solicitud.nombre) equipos.getRange(fila, COL.nombre).setValue(solicitud.nombre);
      respuesta = { estado: 'pendiente' };
    }
  }

  anotar_(libro, ahora, solicitud, respuesta.estado);
  return respuesta;
}

function firmarLicencia_(codigo, nombre, emitida, dias) {
  const vence = new Date(emitida.getTime() + dias * 24 * 60 * 60 * 1000);
  const datos = [PREFIJO, codigo, nombre, emitida.toISOString(), vence.toISOString()].join('|');
  return { datos, firma: firmar_(datos) };
}

function firmar_(datos) {
  return Utilities.base64Encode(Utilities.computeRsaSha256Signature(datos, clavePrivada_(), Utilities.Charset.UTF_8));
}

/**
 * Lee CLAVE_PRIVADA y la deja como PEM bien formado. El campo de Propiedades del script es de una sola línea:
 * se acepta la clave con saltos de línea, con «\n» escritos o sin saltos, y se rearma con líneas de 64 caracteres.
 */
function clavePrivada_() {
  const guardada = PropertiesService.getScriptProperties().getProperty('CLAVE_PRIVADA');
  if (!guardada) throw new Error('Falta la propiedad del script CLAVE_PRIVADA (tools/licencias/LEEME.md).');
  const cuerpo = guardada
    .replace(/\\n/g, '')
    .replace('-----BEGIN PRIVATE KEY-----', '')
    .replace('-----END PRIVATE KEY-----', '')
    .replace(/\s+/g, '');
  const lineas = cuerpo.match(/.{1,64}/g) || [];
  return ['-----BEGIN PRIVATE KEY-----', ...lineas, '-----END PRIVATE KEY-----', ''].join('\n');
}

function diasVigencia_(libro) {
  const dias = Number(libro.getSheetByName(HOJA_CONFIG).getRange('B1').getValue());
  return dias > 0 ? Math.min(dias, DIAS_MAXIMO) : DIAS_POR_DEFECTO;
}

function avisarSolicitud_(libro, solicitud) {
  const correo = String(libro.getSheetByName(HOJA_CONFIG).getRange('B2').getValue()).trim()
    || Session.getEffectiveUser().getEmail();
  try {
    MailApp.sendEmail(correo, `Nueva solicitud de licencia CQEING: ${solicitud.nombre} – ${solicitud.equipo}`,
      `${solicitud.nombre} pidió acceso a los add-ins de CQEING.\n\n` +
      `Equipo: ${solicitud.equipo}\nUsuario de Windows: ${solicitud.usuarioWindows}\nCódigo: ${solicitud.codigo}\n\n` +
      `Para aprobarlo, cambie su estado a «aprobado» en la hoja:\n${libro.getUrl()}`);
  } catch (error) {
    // El aviso es una ayuda: sin cupo de correos la fila igual queda en la hoja.
    console.error(error);
  }
}

function buscarFila_(equipos, codigo) {
  const ultima = equipos.getLastRow();
  if (ultima < 2) return 0;
  const codigos = equipos.getRange(2, COL.codigo, ultima - 1, 1).getValues();
  const indice = codigos.findIndex(([c]) => String(c).trim().toUpperCase() === codigo);
  return indice < 0 ? 0 : indice + 2;
}

function contarPendientes_(equipos) {
  const ultima = equipos.getLastRow();
  if (ultima < 2) return 0;
  return equipos.getRange(2, COL.estado, ultima - 1, 1).getValues()
    .filter(([e]) => String(e).trim().toLowerCase() === 'pendiente').length;
}

function anotar_(libro, ahora, solicitud, respuesta) {
  libro.getSheetByName(HOJA_REGISTRO).appendRow([ahora, solicitud.codigo, solicitud.nombre, solicitud.equipo,
    solicitud.usuarioWindows, respuesta]);
}

function obtenerHoja_(libro, nombre, encabezados) {
  const hoja = libro.getSheetByName(nombre) || libro.insertSheet(nombre);
  if (hoja.getLastRow() === 0) {
    hoja.getRange(1, 1, 1, encabezados.length).setValues([encabezados]).setFontWeight('bold');
    hoja.setFrozenRows(1);
  }
  return hoja;
}

/** Quita lo que rompería el texto firmado (el separador «|» y saltos de línea) y lo recorta. */
function limpiar_(valor) {
  return String(valor == null ? '' : valor).replace(/[|\r\n\t]/g, ' ').trim().slice(0, MAX_TEXTO);
}

function responder_(objeto) {
  return ContentService.createTextOutput(JSON.stringify(objeto)).setMimeType(ContentService.MimeType.JSON);
}

/**
 * Comprobación previa (Tarea 1): firma un texto fijo con la clave guardada. Lo que imprime, junto con publica.pem,
 * va a gestor/CQEING.Gestor.Core.Tests/Datos/firma-apps-script.json para que .NET pruebe que verifica esta firma.
 */
function probarFirma() {
  const datos = [PREFIJO, '0000-0000-0000-0000', 'Yina Londoño', '2026-10-28T00:00:00.000Z', '2026-10-31T00:00:00.000Z'].join('|');
  console.log(JSON.stringify({ datos, firma: firmar_(datos) }, null, 2));
}
