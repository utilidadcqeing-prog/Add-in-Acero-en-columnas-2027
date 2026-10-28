# Plan: Licencias de los add-ins con una hoja de Google (reunión 28-oct-2026)

> **Para quien lo implemente (persona o agente):** se sigue tarea por tarea, en orden, con los pasos `- [ ]`.
> Cada tarea termina con sus pruebas en verde y un commit (archivos por nombre, sin `git add -A`, sin marca de Claude).
> Lo que este plan no fija se decide aquí primero (Decisiones / Hallazgos) y después se programa.

**Objetivo:** que todos los add-ins de CQEING solo funcionen en los equipos aprobados en una hoja de Google, y que
quien aprueba vea quién los usa.

**Arquitectura:** cada equipo tiene un código fijo que sale del `MachineGuid` de Windows. El gestor pide la licencia a
un Apps Script publicado como aplicación web sobre la hoja «Licencias CQEING». Si la fila de ese código dice
`aprobado`, el script devuelve una licencia de pocos días **firmada con RSA**. Al abrir Revit, cada add-in comprueba
tres cosas: la firma, con la clave pública compilada en `addins/compartido`; que el código sea el del equipo; y que no
haya vencido. Sin licencia vigente, el add-in no crea sus botones. La licencia se renueva sola en segundo plano.

**Tecnología:** .NET 8/10 (`System.Security.Cryptography.RSA`, `System.Text.Json`, `Microsoft.Win32.Registry`),
xUnit, Google Sheets + Apps Script (`Utilities.computeRsaSha256Signature`, `MailApp`, `LockService`), OpenSSL de
Git for Windows para crear las claves.

**Propuesta de la que sale:** `Desktop\addin vigas\seguridad-licencias-propuesta.md`.

## Contexto

- **Quién lo pidió:** Carlos Quintero, en la reunión del 28-oct-2026 (`Desktop\addin vigas\Meeting Transcription (28.txt`,
  30:21–39:50).
- **El problema:** cualquiera que copie el `.exe` del gestor o la carpeta de add-ins los puede usar en otro PC.
  Es **ruta crítica**, por delante de viguetas.
- **Lo que le importa a Carlos:** que las copias ya instaladas dejen de funcionar, y saber quién las tiene.
- **Opción elegida** (Juan, 28-oct): la B, hoja de Google, en la **cuenta de Google del ingeniero Carlos**.
  Quien aprueba solo cambia una celda.
- **Alcance:** los 4 add-ins (acero en vigas, armado de columnas, cuadro de cantidades, imágenes) y el gestor con su
  componente dentro de Revit.
- **Fuera de alcance:** panel web propio, ofuscar las DLL y revocar al instante.
- **Entorno:** rama `seguridad-licencias` desde `main`; Revit 2026 y 2027.

## Criterios de aceptación

- [ ] En un equipo sin licencia, la pestaña CQEING solo muestra el panel «Gestor» y un único aviso con el código del equipo.
- [ ] Desde el gestor, «Solicitar acceso» crea una fila `pendiente` en la hoja y manda un correo a la cuenta que aprueba.
- [ ] Al poner `aprobado`, el gestor recibe la licencia. Al reiniciar Revit aparecen los 4 add-ins.
- [ ] Copiar `licencia.json` a otro PC no sirve. Editarlo a mano tampoco.
- [ ] Al poner `revocado`, la licencia se borra en la siguiente conexión. Si el equipo no se conecta, deja de funcionar
      al vencer.
- [ ] Cada consulta queda en la pestaña «Registro».
- [ ] Sin internet, una licencia vigente sigue funcionando y Revit arranca sin errores.
- [ ] Un add-in nuevo no puede olvidar la comprobación: lo vigila una prueba.

## Hallazgos del código (verificados el 28-oct en `origin/main`)

1. **El repo es público.** Se comprobó con la API de GitHub sin iniciar sesión: `visibility: public`, y las releases se
   listan sin token. Mientras el código sea público, la comprobación se puede quitar. La Tarea 8 lo cierra.
2. **Un solo archivo sirve para todos.** `addins/Directory.Build.props` compila `addins/compartido/*.cs` dentro de cada
   proyecto bajo `addins/`, también los de `acero-vigas/calculo`. Las 5 `IExternalApplication`:
   - `addins/acero-vigas/AppVigas.cs` (`using CQEING.Compartido;`)
   - `addins/armado-columnas/App.cs` (`using CQEING.Compartido;`)
   - `addins/cuadro-cantidades/App/RevitApplication.cs` (**sin** `using CQEING.Compartido;`, hay que agregarlo)
   - `addins/imagenes/AppImagenes.cs` (`using CQEING.Compartido;`)
   - `addins/gestor-revit/AppGestorRevit.cs`
3. **El componente del gestor ya trabaja al abrir Revit.** `addins/gestor-revit` referencia
   `gestor/CQEING.Gestor.Core` y ya hace trabajo en segundo plano al arrancar (`AvisoNovedades.Empezar`: `Task.Run`, y
   vuelve al hilo de Revit con `Dispatcher.InvokeAsync`). La renovación sigue ese mismo patrón.
4. **El componente del gestor carga el último** (`CQEING.zz-gestor-revit.addin`). La licencia que renueve al arrancar
   vale para la **siguiente** apertura de Revit (Decisión 4).
5. **Estilo de las pruebas.** Nombres en español con guiones bajos (`Al_abrir_muestra_...`). Ayudas que ya existen:
   - `Core.Tests/Apoyo/HandlerFalso(Func<HttpRequestMessage, HttpResponseMessage>)`, con `.Pedidos` y `.Cliente()`.
   - `Core.Tests/Apoyo/CarpetaTemporal` (`.Ruta`, `.Combinar(...)`).
   - `Gestor.Tests/Apoyo/Entorno`, que arma la `VistaGestor` con reloj fijo `2026-10-17 09:05`.
6. **El nombre del repo aparece en tres sitios:** `Repositorio.Nombre` (lo usan `FuenteReleases` y
   `ActualizadorVelopack`) y `addins/gestor-revit/LanzadorGestor.cs:11` (`EnlaceDescarga`). Ninguna prueba lo fija.
7. **Las clases de `compartido` son `internal`** y cada DLL tiene su propia copia. Por eso solo el componente del gestor
   muestra el aviso (Decisión 6).
8. **Apps Script no entrega la IP** de quien llama. El registro guarda el nombre del PC y el usuario de Windows.

9. **La firma de Apps Script es compatible** (2026-10-28). `probarFirma()`, en la hoja «Licencias CQEING»
   (`1YMVkB5dbNUwlFhjbCCS1He_uLYm2E5CBPNXAP8t--Oo`), firmó el texto de prueba con «Yina Londoño». La firma de
   256 bytes se verificó con `publica.pem`: `openssl dgst -sha256 -verify publica.pem -signature firma.bin datos.txt`
   dio `Verified OK`. Los datos ocupan 102 bytes, así que la «ñ» va en UTF-8. Es RSA PKCS#1 v1.5 con SHA-256, lo mismo
   que `RSASignaturePadding.Pkcs1` en .NET. La prueba en .NET queda en la Tarea 2, con
   `gestor/CQEING.Gestor.Core.Tests/Datos/firma-apps-script.json`.
10. **`configurar()` armó bien la hoja** (revisado con el conector de Google Sheets el 2026-10-28): las pestañas
    `Equipos`, `Registro` y `Config` con sus encabezados y la primera fila fija; la lista estricta
    `pendiente/aprobado/revocado` en `Equipos!E`; `Config!B1 = 3`. «Hoja 1» se borró.

11. **El servicio responde bien** (2026-10-28, implementación de prueba de Juan:
    `https://script.google.com/macros/s/AKfycbyQoyrSmRegqFWt-rVB0JfNWCCxLFocczDYpbfiEoATqaLSClCR9wZR5MvEG7jboQ/exec`).
    La URL redirige y `Invoke-RestMethod` y `curl -L` siguen la redirección y reciben el JSON. Resultados:
    - `?codigo=xx` da `{"error":"codigo"}`;
    - un código nuevo crea **una** fila `pendiente` («Prueba Ñandú» se guardó bien);
    - una segunda consulta no duplica la fila, solo actualiza «Última conexión» y «Versión gestor»;
    - con `aprobado` llega `{"estado":"aprobado","licencia":…}`. La firma se verificó con `publica.pem` (`crypto.verify`
      de Node: `true`). Vigencia de 72 h. El nombre firmado es el de la hoja, no el que mandó el equipo. Las fechas
      llevan milisegundos (`2026-10-28T17:03:05.352Z`), y la Tarea 2 debe leerlas así;
    - con `revocado` llega `{"estado":"revocado"}`;
    - `Registro` guardó las 4 consultas con su respuesta;
    - el correo de aviso de la solicitud nueva llegó (confirmado por Juan).
    La fila de prueba es `0000-0000-0000-0000`. Esta URL es **provisional**: la definitiva es la que salga de la
    implementación de Carlos (Decisión 10).
12. **Al archivar una implementación, su URL deja de responder** (2026-10-28). Da HTTP 404 con una página HTML de
    Google, y .NET lo toma como `SinConexion`, como debe ser. Una implementación de tipo «Biblioteca» da una URL
    `…/macros/library/d/…/1` que no sirve: tiene que ser «Aplicación web». La implementación de prueba actual es
    `https://script.google.com/macros/s/AKfycbxoTN5H3JjdQGcG2nRngX9D-hQBU3kKOjtZhgUxbulFovbHDTivCxFj0YB0WdV4-bLB/exec`
    y está en `ServicioLicencias.Url`. Desde .NET (`ServicioLicencias.PedirAsync` con `ClienteHttp.Crear()`), la fila
    de prueba respondió `Revocada`, y la consulta quedó en `Registro`: `HttpClient` sigue bien la redirección.

## Decisiones

1. **Hoja de Google + Apps Script, sin servidor propio**, en la cuenta del ingeniero Carlos (2026-10-28, Juan).
2. **Firma RSA 2048, PKCS#1 v1.5 con SHA-256**, que es lo que hace `computeRsaSha256Signature`.
   - La clave privada vive solo en las propiedades del script (`CLAVE_PRIVADA`).
   - La pública va como constante en `addins/compartido/Licencia.cs`.
   - Ningún archivo `.pem` de producción entra al repo.
3. **Lo que se firma es un texto, no un JSON:** `cqeing-licencia-v1|<código>|<nombre>|<emitida>|<vence>`.
   - Se firma en UTF-8, con las fechas ISO-8601 en UTC, tal como las da `Date.toISOString()`.
   - El nombre llega sin `|`.
   - Así el script y .NET trabajan exactamente sobre los mismos bytes.
4. **Vigencia de 3 días**, en la celda `Config!B1`, que Carlos puede cambiar. Se pide una licencia nueva cuando la actual
   se emitió hace más de **12 horas**. Es el punto medio entre las «24 h» que dijo Carlos y poder trabajar en obra sin internet.
5. **Código del equipo** = los primeros 8 bytes de `SHA-256("CQEING|" + MachineGuid)` en hexadecimal mayúscula, con el
   formato `XXXX-XXXX-XXXX-XXXX`.
6. **Sin licencia, los add-ins no crean sus botones y no avisan.** El componente del gestor siempre crea su botón, para
   que se pueda pedir acceso. Es el único que avisa, una vez por cada apertura de Revit.
7. **Nunca se guarda una licencia que no se haya verificado** antes con la clave pública y el código del equipo.
8. **Reloj atrasado:** se anota la última fecha vista. Si la hora del equipo queda más de 1 hora antes de esa fecha, o
   antes de la fecha de emisión, la licencia no vale.
9. **No hay atajo para desarrollo** (ni `#if DEBUG` ni variables de entorno). El PC de quien desarrolla pide licencia
   como cualquier otro.
10. **La hoja la arma Juan y se la transfiere a Carlos** (2026-10-28, Juan). Después de la transferencia, Carlos hace
    una **nueva implementación** desde su cuenta, porque «Ejecutar como: yo» corre con la cuenta de quien implementó.
    La URL que va en el código es la de Carlos. Pasos en `tools/licencias/LEEME.md`, sección 5.
    **La transferencia se hace después de la prueba en Revit (Tarea 7, #196)** (2026-10-28, Juan). Hasta entonces el
    código usa la URL de prueba de Juan (Hallazgo 11), y la de Carlos la reemplaza antes de publicar (Tarea 8).
12. **El aviso de Revit es una ventana como `VentanaAviso`, no un `TaskDialog`** (2026-10-28, al implementar la #194).
    `VentanaLicencia` se muestra cuando Revit queda libre (`DispatcherPriority.ApplicationIdle`, el mismo patrón de
    `AvisoNovedades`). Así no frena el arranque ni queda detrás del logo de carga. No es modal, tiene «Más tarde» y
    «Abrir el gestor», y no se cierra sola. Texto: el motivo con el código del equipo y «Abra el Gestor de add-ins y
    pulse "Solicitar acceso". Si ya lo pidió y se lo aprobaron, reinicie Revit.». El registro de soporte de
    `AvisoNovedades` pasó a `RegistroSoporte`, compartido; la licencia escribe en `licencia-revit.log`.
13. **La consulta de la licencia al abrir el gestor no ocupa la ventana** (2026-10-28, al implementar la #195).
    `VistaGestor.ConsultarLicenciaAsync` corre a la par de «Buscar novedades» y no pasa por `OcuparAsync`, así las
    tarjetas y sus botones no esperan a Google. Mientras consulta, «Solicitar acceso» queda deshabilitado para no mandar
    dos pedidos a la vez. El aviso «Licencia recibida. Reinicie Revit…» sale solo al pulsar «Solicitar acceso» y solo si
    antes no había licencia vigente: al abrir, «Buscar novedades» reescribe la franja de aviso y lo taparía.
11. **`configurar()` arma la hoja** (pestañas, encabezados, lista de estados, columna de código como texto), para no
    hacerlo a mano. Si `Config!B2` está vacío, el aviso va a la cuenta que implementó el script.

## Restricciones globales

- **Archivos**, todos en `%LOCALAPPDATA%\CQEING\`: `licencia.json`, `licencia-vista.txt` (la última fecha vista) y
  `licencia-nombre.txt` (el nombre de quien pide).
- **Formato de `licencia.json`:** `{"datos":"cqeing-licencia-v1|…","firma":"<base64>"}`.
- **Estados de la hoja**, exactos y en minúscula: `pendiente`, `aprobado`, `revocado`.
- **`OnStartup` nunca lanza por la licencia.** Cualquier error de disco, de registro o de formato cuenta como sin licencia.
- **Textos al usuario** en español neutro, sin voseo.

## Revisión: lo que más puede fallar

1. **Nombres con tildes o eñe** («Yina Londoño»): si cada lado usa otro juego de caracteres, la firma nunca coincide.
   Lo prueban la Tarea 1 (firma real de Apps Script) y la Tarea 2.
2. **Sin internet al abrir Revit:** la renovación falla en silencio y la licencia vigente sigue valiendo. Lo prueba la Tarea 3.
3. **Respuesta `aprobado` con una licencia de otro equipo o mal firmada:** no debe reemplazar la buena. Lo prueba la Tarea 3.
4. **`licencia.json` vacío o cortado a mitad:** da `Invalida`, sin excepción, y la escritura es atómica. Lo prueban las
   Tareas 2 y 3.
5. **Reloj atrasado para revivir una licencia vencida:** da `RelojAtrasado`. Lo prueba la Tarea 2.

---

## Plan paso a paso

### Tarea 1: Hoja, claves y script (la arma Juan y la transfiere a Carlos, Decisión 10)

Va primero porque fija el formato que firma el script, y el resto se adapta a eso.

**Archivos:**
- Crear: `tools/licencias/servicio.gs`, `tools/licencias/LEEME.md`
- Crear: `gestor/CQEING.Gestor.Core.Tests/Datos/firma-apps-script.json`
- Modificar: `.gitignore` (agregar `*.pem`)

**La hoja «Licencias CQEING»:** solo Carlos es editor.
- `Equipos`: Código | Nombre | Equipo | Usuario Windows | Estado | Solicitada | Última conexión | Versión gestor | Notas.
  La columna «Estado» tiene validación de lista con `pendiente,aprobado,revocado`.
- `Registro`: Fecha | Código | Nombre | Equipo | Usuario Windows | Respuesta.
- `Config`: `A1` «Días de vigencia» y `B1` `3`; `A2` «Correo de aviso» y `B2` el correo de Carlos.

**`servicio.gs`, función `doGet(e)`.** Recibe `codigo`, `nombre`, `equipo`, `usuarioWindows` y `version`:
1. Valida `codigo` con `^[0-9A-F]{4}(-[0-9A-F]{4}){3}$`. Si no cumple, responde `{"error":"codigo"}`. Los demás textos
   se recortan, se limitan a 60 caracteres y se les quita el `|`.
2. Toma `LockService.getScriptLock().waitLock(10000)` para no duplicar filas.
3. Si no hay fila para ese código, la agrega como `pendiente` y avisa con `MailApp.sendEmail` a `Config!B2`
   («Nueva solicitud: {nombre} – {equipo}»). Si ya hay 50 filas `pendiente`, no agrega más.
4. Actualiza «Última conexión» y «Versión gestor», y anota la consulta en `Registro`.
5. Si la fila está en `aprobado`, firma `cqeing-licencia-v1|{codigo}|{nombre de la hoja}|{emitida}|{vence}` (Decisión 3)
   con `Utilities.base64Encode(Utilities.computeRsaSha256Signature(datos, CLAVE_PRIVADA, Utilities.Charset.UTF_8))`.
   Se firma el nombre de la hoja, no el que mandó el equipo, porque Carlos puede corregirlo.
6. Responde `{"estado":"aprobado","licencia":{"datos":…,"firma":…}}`, `{"estado":"pendiente"}` o `{"estado":"revocado"}`
   con `ContentService` y tipo JSON.

Además, una función `probarFirma()` que firma `cqeing-licencia-v1|0000-0000-0000-0000|Yina Londoño|2026-10-28T00:00:00.000Z|2026-10-31T00:00:00.000Z`
y escribe `datos` y `firma` en el registro del editor de Apps Script.

- [ ] **Paso 1: Crear las claves.** Una sola vez, en Git Bash y fuera del repo:
      `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out privada.pem` y luego
      `openssl pkey -in privada.pem -pubout -out publica.pem`.
- [ ] **Paso 2: Montar la hoja y el script.** Crear la hoja y pegar `servicio.gs`. Guardar el contenido de `privada.pem`
      en Propiedades del script como `CLAVE_PRIVADA`. Implementar como aplicación web, con «Ejecutar como: yo» y
      «Quién tiene acceso: cualquier usuario». Anotar la URL `/exec`.
- [ ] **Paso 3: Comprobación previa** (lo que estaba sin verificar).
  - Correr `probarFirma()` y guardar `{ "clavePublica": <publica.pem>, "datos": …, "firma": … }` en
    `Datos/firma-apps-script.json`. Nada de eso es secreto.
  - Abrir en el navegador `<url>/exec?codigo=0000-0000-0000-0000&nombre=Prueba&equipo=PC&usuarioWindows=u&version=0`:
    debe aparecer la fila `pendiente`, llegar el correo y responder `{"estado":"pendiente"}`.
  - Desde PowerShell, `Invoke-RestMethod` con la misma URL debe devolver el mismo JSON, lo que confirma que la
    redirección se sigue.
  - Si algo falla, anotarlo en Hallazgos y cambiar el plan antes de seguir.
- [ ] **Paso 4: Guardar la clave privada.** Borrar `privada.pem` del disco después de guardarla en un lugar seguro que
      decida Carlos; si se pierde, se crea un par nuevo. Guardar `publica.pem` para la Tarea 2.
- [ ] **Paso 5: Documentar.** `LEEME.md` con los pasos 1 a 4, para poder rehacerlo: otra cuenta o cambio de clave.
- [ ] **Paso 6: Commit** `feat(licencias): hoja de Google y script que firma las licencias`. Revisar que no vaya ningún `.pem`.

### Tarea 2: Código del equipo y verificación de la licencia

**Archivos:**
- Crear: `addins/compartido/CodigoEquipo.cs` y `addins/compartido/Licencia.cs`
- Modificar: `gestor/CQEING.Gestor.Core/CQEING.Gestor.Core.csproj`
- Probar: `gestor/CQEING.Gestor.Core.Tests/CodigoEquipoTests.cs` y `gestor/CQEING.Gestor.Core.Tests/LicenciaTests.cs`

**Lo que produce.** Todo es `internal`, en el namespace `CQEING.Compartido`, y no usa nada de la API de Revit:

```csharp
internal static class CodigoEquipo
{
    public static string Desde(string machineGuid);   // Decisión 5
    public static string? Actual();                   // HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid con RegistryView.Registry64; null si falla
}

internal enum ResultadoLicencia { Vigente, NoExiste, Invalida, OtroEquipo, Vencida, RelojAtrasado }

internal sealed record EstadoLicencia(ResultadoLicencia Resultado, string? Nombre, DateTime? EmitidaUtc, DateTime? VenceUtc)
{
    public bool EsVigente => Resultado == ResultadoLicencia.Vigente;
    public string Motivo(string? codigoEquipo);       // texto para el usuario, termina con «Código del equipo: …»
}

internal static class Licencia
{
    public const string ClavePublica = "…";           // contenido de publica.pem (Tarea 1)
    public static string CarpetaPorDefecto { get; }   // %LOCALAPPDATA%\CQEING
    public static EstadoLicencia Verificar(string? contenido, string? codigoEquipo, DateTime ahoraUtc, DateTime? ultimaVistaUtc, string clavePublicaPem);
    public static EstadoLicencia Leer(string carpeta, string? codigoEquipo, DateTime ahoraUtc, string clavePublicaPem);
    public static bool Vigente(out string motivo);    // Leer(CarpetaPorDefecto, CodigoEquipo.Actual(), DateTime.UtcNow, ClavePublica); nunca lanza
    public static void AnotarVista(string carpeta, DateTime ahoraUtc);   // guarda el máximo; los errores de disco se ignoran
}
```

- [ ] **Paso 1: Escribir las pruebas, que deben fallar.** En las pruebas se crea un par de claves con `RSA.Create(2048)`
      y una ayuda `Firmar(string datos)` que firma en UTF-8 con `SHA256` y `RSASignaturePadding.Pkcs1`.
  - `El_mismo_guid_da_el_mismo_codigo_con_formato_XXXX`: el código cumple `^[0-9A-F]{4}(-[0-9A-F]{4}){3}$`.
  - `Guids_distintos_dan_codigos_distintos`
  - `La_firma_hecha_por_Apps_Script_se_verifica`: lee `Datos/firma-apps-script.json` y con su clave da `Vigente` para
    `0000-0000-0000-0000`, `Nombre == "Yina Londoño"`, con `ahoraUtc = 2026-10-29`.
  - `Licencia_bien_firmada_es_vigente`
  - `Datos_editados_sin_volver_a_firmar_es_invalida`
  - `Firmada_con_otra_clave_es_invalida`
  - `Codigo_de_otro_equipo_es_OtroEquipo`
  - `Pasado_el_vencimiento_es_Vencida`
  - `Reloj_dos_horas_antes_de_la_ultima_vista_es_RelojAtrasado`
  - `Reloj_dos_horas_antes_de_la_emision_es_RelojAtrasado`
  - `Contenido_nulo_es_NoExiste_y_vacio_o_cortado_es_Invalida`: `null`, `""` y `{"datos":"cqe` no lanzan.
  - `Version_desconocida_es_invalida`: el prefijo es `cqeing-licencia-v2`.
  - `Leer_una_carpeta_que_no_existe_es_NoExiste`
  - `Sin_codigo_de_equipo_es_Invalida`: `codigoEquipo` es `null`.
  - `AnotarVista_nunca_retrocede`: anotar las 10:00 y luego las 08:00 deja las 10:00.
- [ ] **Paso 2: Comprobar que fallan.** `dotnet test gestor/CQEING.Gestor.Core.Tests --filter "FullyQualifiedName~Licencia|FullyQualifiedName~CodigoEquipo"`
      → falla al compilar.
- [ ] **Paso 3: Implementar.**
  - En `CQEING.Gestor.Core.csproj`:
    `<Compile Include="..\..\addins\compartido\Licencia.cs;..\..\addins\compartido\CodigoEquipo.cs" Link="Compartido\%(Filename)%(Extension)" />`,
    `<InternalsVisibleTo Include="CQEING.Gestor.Core.Tests" />` y el JSON de prueba con `CopyToOutputDirectory` en el
    proyecto de pruebas.
  - Las fechas se leen con `TryParseExact` y el formato exacto de `toISOString()`, `yyyy-MM-dd'T'HH:mm:ss.fff'Z'`, con
    `AssumeUniversal | AdjustToUniversal`. *(Corregido el 2026-10-28: `RoundtripKind` no se puede combinar con
    `AdjustToUniversal`, y el formato exacto rechaza fechas que el script nunca escribiría.)*
  - `CodigoEquipo.Actual()` comprueba `OperatingSystem.IsWindows()` antes de leer el registro. El núcleo es `net8.0`
    sin `-windows`, y así el analizador de plataforma no avisa.
- [ ] **Paso 4: Comprobar que pasan.** El mismo comando → pasan. Además
      `dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2027` y
      `dotnet test addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests` siguen en verde, porque también compilan `compartido`.
- [ ] **Paso 5: Commit** `feat(licencias): código del equipo y verificación de la licencia firmada`.

### Tarea 3: Pedir y renovar la licencia (núcleo del gestor)

**Archivos:**
- Crear: `gestor/CQEING.Gestor.Core/ServicioLicencias.cs` y `gestor/CQEING.Gestor.Core/RenovadorLicencia.cs`
- Probar: `gestor/CQEING.Gestor.Core.Tests/ServicioLicenciasTests.cs` y `gestor/CQEING.Gestor.Core.Tests/RenovadorLicenciaTests.cs`

**Usa:** `Licencia.Verificar`, `Licencia.AnotarVista` y `CodigoEquipo.Actual` (Tarea 2).

**Lo que produce** (todo público):

```csharp
public enum EstadoSolicitud { Aprobada, Pendiente, Revocada, SinConexion }
public sealed record RespuestaServicio(EstadoSolicitud Estado, string? ContenidoLicencia);
public sealed record DatosSolicitud(string Codigo, string Nombre, string Equipo, string UsuarioWindows, string VersionGestor);

public sealed class ServicioLicencias(HttpClient http, string url = ServicioLicencias.Url)
{
    public const string Url = "…/exec";   // URL de la Tarea 1
    public Task<RespuestaServicio> PedirAsync(DatosSolicitud datos, CancellationToken ct = default);
}

public sealed record InfoLicencia(EstadoSolicitud? UltimaRespuesta, bool Vigente, string? Nombre, DateTime? VenceUtc, string Motivo, string? CodigoEquipo);

public sealed class RenovadorLicencia(ServicioLicencias servicio, string carpeta, Func<DateTime> ahoraUtc, string? codigoEquipo, string clavePublicaPem)
{
    public static RenovadorLicencia Crear(HttpClient http);   // carpeta, reloj, código y clave reales
    public string? NombreGuardado { get; }                    // licencia-nombre.txt
    public InfoLicencia Estado();
    public Task<InfoLicencia> RenovarAsync(string? nombre, bool forzar, string versionGestor, CancellationToken ct = default);
}
```

**Cómo pide `PedirAsync`:** hace un GET con los cinco datos en la query (`codigo`, `nombre`, `equipo`, `usuarioWindows`,
`version`), cada uno pasado por `Uri.EscapeDataString`. Devuelve `SinConexion` si pasa cualquiera de estas cosas:
`HttpRequestException`, se agota el tiempo, la respuesta no es 2xx, el JSON no se entiende o llega `error`. Con
`aprobado`, `ContenidoLicencia` es el objeto `licencia` serializado tal cual (`{"datos","firma"}`).

**Qué hace `RenovarAsync`:**
1. Si recibe `nombre`, lo guarda en `licencia-nombre.txt`. Si no, usa el guardado y, si no hay, `Environment.UserName`.
2. Sin `forzar`, no consulta si la licencia es vigente y se emitió hace menos de 12 h.
3. Según la respuesta:
   - `Aprobada`: pasa por `Verificar` con el código del equipo y solo se escribe si da `Vigente`. Se escribe en un
     temporal con nombre único y luego `File.Move(..., overwrite: true)`.
   - `Revocada`: borra `licencia.json`.
   - `Pendiente` o `SinConexion`: no toca nada.
4. Siempre llama a `AnotarVista`.

- [ ] **Paso 1: Escribir las pruebas, que deben fallar.** Usan `HandlerFalso` y `CarpetaTemporal`.
  - `Respuesta_aprobado_devuelve_Aprobada_con_la_licencia`
  - `Respuesta_pendiente_devuelve_Pendiente` y `Respuesta_revocado_devuelve_Revocada`
  - `Sin_red_error_500_o_json_ilegible_devuelven_SinConexion`
  - `Manda_los_cinco_datos_en_la_query`: la URL contiene `nombre=Yina%20Londo%C3%B1o`.
  - `Aprobada_guarda_la_licencia_y_queda_vigente`
  - `Sin_conexion_conserva_la_licencia_vigente`
  - `Aprobada_de_otro_equipo_no_reemplaza_la_buena` y `Aprobada_mal_firmada_no_reemplaza_la_buena`
  - `Revocada_borra_la_licencia`
  - `Emitida_hace_menos_de_12_horas_no_consulta`: `Pedidos` queda vacío. Y `Forzada_consulta_igual`.
  - `Sin_nombre_usa_el_guardado`
- [ ] **Paso 2: Comprobar que fallan.** `dotnet test gestor/CQEING.Gestor.Core.Tests --filter "FullyQualifiedName~Licencia"`
      → falla al compilar.
- [ ] **Paso 3: Implementar** según las interfaces de arriba.
- [ ] **Paso 4: Comprobar que pasan.** El mismo comando pasa, y también `dotnet test gestor/CQEING.Gestor.Core.Tests` completo.
- [ ] **Paso 5: Commit** `feat(licencias): pedir y renovar la licencia desde el gestor`.

### Tarea 4: Los 4 add-ins exigen la licencia al abrir Revit

**Archivos:**
- Modificar: `addins/acero-vigas/AppVigas.cs`, `addins/armado-columnas/App.cs`,
  `addins/cuadro-cantidades/App/RevitApplication.cs` (también su `using CQEING.Compartido;`) y `addins/imagenes/AppImagenes.cs`
- Probar: `gestor/CQEING.Gestor.Core.Tests/TodosLosAddinsExigenLicenciaTests.cs`

**Usa:** `Licencia.Vigente(out string)` (Tarea 2).

- [ ] **Paso 1: Escribir la prueba, que debe fallar.** `Cada_IExternalApplication_salvo_el_gestor_pregunta_por_la_licencia`:
      desde la raíz del repo, recorre los `.cs` de `addins/` que contienen `: IExternalApplication`, deja fuera
      `addins/gestor-revit`, y exige que cada uno contenga `Licencia.Vigente(`. El mensaje de error lista los que faltan.
- [ ] **Paso 2: Comprobar que falla.** `dotnet test gestor/CQEING.Gestor.Core.Tests --filter TodosLosAddinsExigenLicencia`
      → falla con los 4 archivos.
- [ ] **Paso 3: Implementar.** Primera línea de cada `OnStartup`: `if (!Licencia.Vigente(out _)) return Result.Succeeded;`.
- [ ] **Paso 4: Comprobar.** La prueba pasa, y los 4 proyectos compilan con `-p:RevitVersion=2026` y con `=2027`.
- [ ] **Paso 5: Commit** `feat(licencias): los add-ins no cargan sin licencia vigente`.

### Tarea 5: El componente del gestor en Revit avisa y renueva

**Archivos:**
- Crear: `addins/gestor-revit/AvisoLicencia.cs`
- Modificar: `addins/gestor-revit/AppGestorRevit.cs`

**Usa:** `Licencia.Vigente` (Tarea 2), `RenovadorLicencia.Crear` y `RenovadorLicencia.RenovarAsync` (Tarea 3).

- [ ] **Paso 1: Implementar.** En `AppGestorRevit.OnStartup`, después de crear el botón:
  - Si `Licencia.Vigente(out motivo)` es falso, `AvisoLicencia.Mostrar(motivo)` abre un `TaskDialog` con título
    «Licencia de CQEING», el motivo y el texto «Abra el Gestor de add-ins y pulse "Solicitar acceso".».
  - Luego, con el patrón de `AvisoNovedades.Empezar` (`Task.Run` y `try/catch` que no deja escapar nada, con anotación
    en un log como `Anotar`): `RenovadorLicencia.Crear(ClienteHttp.Crear()).RenovarAsync(null, false, <versión del
    componente>)`. No se espera y no se toca la cinta.
- [ ] **Paso 2: Comprobar que compila.** `dotnet build addins/gestor-revit/GestorRevit.csproj -c Debug -p:RevitVersion=2026` y `=2027`.
- [ ] **Paso 3: Commit** `feat(licencias): aviso y renovación de la licencia al abrir Revit`.

La prueba de esta tarea es en Revit (Tarea 7), porque `TaskDialog` y `OnStartup` no corren fuera de Revit.

### Tarea 6: Licencia en la ventana del gestor

**Archivos:**
- Modificar: `gestor/CQEING.Gestor/VistaGestor.cs`, `gestor/CQEING.Gestor/VentanaPrincipal.xaml` y `gestor/CQEING.Gestor/Program.cs`
- Modificar: `gestor/CQEING.Gestor.Tests/Apoyo/Entorno.cs`. Se le agrega un servicio de licencias falso: un
  `HttpMessageHandler` propio, como `GitHubFalso`, que responde el JSON que pida cada prueba, con un par de claves de
  prueba y el código fijo `AAAA-BBBB-CCCC-DDDD`.
- Probar: `gestor/CQEING.Gestor.Tests/VistaGestorLicenciaTests.cs`

**Usa:** `RenovadorLicencia` e `InfoLicencia` (Tarea 3). El constructor de `VistaGestor` recibe además un
`RenovadorLicencia`. `Program.Main` le pasa `RenovadorLicencia.Crear(ClienteHttp.Crear())`.

**Lo que se agrega a `VistaGestor`:** `string TextoLicencia`, `string? CodigoEquipo`, `bool PideNombre`,
`string NombreSolicitante` (editable), `ComandoAsync SolicitarAcceso` (llama a `RenovarAsync(NombreSolicitante, forzar: true, …)`)
y `ComandoAsync CopiarCodigo`. Al abrir la ventana se llama `RenovarAsync(null, false, …)` sin frenar la carga de las tarjetas.

**Textos exactos de `TextoLicencia`:**
- vigente: `Licencia vigente hasta el {vence:dd-MM-yyyy} · {nombre}`
- pendiente: `Solicitud enviada. Falta que la aprueben. Código del equipo: {código}`
- sin licencia: `Este equipo no tiene licencia. Código del equipo: {código}`
- revocada: `El acceso de este equipo fue retirado. Código del equipo: {código}`
- sin conexión y sin licencia: `No se pudo consultar la licencia (sin conexión). Código del equipo: {código}`

Después de una `Aprobada` nueva, `Aviso` = `Licencia recibida. Reinicie Revit para usar los add-ins.`

- [ ] **Paso 1: Escribir las pruebas, que deben fallar.**
  - `Al_abrir_con_licencia_vigente_muestra_hasta_cuando_y_el_nombre`
  - `Sin_licencia_pide_nombre_y_no_deja_solicitar_con_el_nombre_vacio`
  - `Solicitar_acceso_pendiente_muestra_el_texto_de_pendiente`
  - `Solicitar_acceso_aprobado_muestra_vigente_y_pide_reiniciar_Revit`
  - `Sin_conexion_y_sin_licencia_muestra_el_texto_sin_conexion`
  - `Revocada_muestra_el_texto_de_acceso_retirado`
- [ ] **Paso 2: Comprobar que fallan.** `dotnet test gestor/CQEING.Gestor.Tests --filter VistaGestorLicencia` → falla.
- [ ] **Paso 3: Implementar la vista.** En `VentanaPrincipal.xaml` va una franja encima de las tarjetas con
      `TextoLicencia`, el código del equipo con un botón «Copiar» y, si `PideNombre`, un `TextBox` «Su nombre» y el
      botón «Solicitar acceso».
- [ ] **Paso 4: Comprobar.** Las pruebas pasan, y también `dotnet test gestor/CQEING.Gestor.Tests` completo, que incluye
      las de `VistaGestorTests` con el `Entorno` cambiado. Abrir con `dotnet run --project gestor/CQEING.Gestor`,
      mirar la franja y guardar una captura en `docs/planes/imagenes/2026-10-28-licencia-gestor.png`.
- [ ] **Paso 5: Commit** `feat(licencias): estado y solicitud de acceso en el gestor`.

### Tarea 7: Prueba en Revit

- [ ] **Desplegar.** Con Revit cerrado (`Get-Process -Name Revit -ErrorAction SilentlyContinue` no devuelve nada),
      desplegar con `powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin <id> -Revit 2027` los
      4 add-ins y el componente del gestor.
- [ ] **Sin `licencia.json`:** solo el panel «Gestor» y un único aviso con el código.
- [ ] **Solicitar acceso desde el gestor:** aparece la fila `pendiente` y llega el correo a Carlos.
- [ ] **Poner `aprobado`, pulsar de nuevo «Solicitar acceso» y reiniciar Revit:** aparecen los 4 paneles.
- [ ] **Editar a mano la fecha `vence` de `licencia.json`:** no cargan.
- [ ] **Copiar `licencia.json` a otro PC:** no cargan.
- [ ] **Sin red, con la licencia vigente:** cargan, y Revit no tarda más en abrir.
- [ ] **Poner `revocado` y abrir Revit con red dos veces:** a la segunda ya no cargan.
- [ ] **Registro:** quedan las capturas en `docs/planes/imagenes/2026-10-28-licencia-*.png` y el resultado en Estado.

### Tarea 8: Publicar y cerrar el repo

El orden importa: **primero el gestor**, porque trae el componente que pide la licencia. Quien actualice los add-ins
sin tener el gestor nuevo se queda sin botones y sin forma de pedir acceso.

- [ ] **Repo nuevo.** Crear el repo público `utilidadcqeing-prog/CQEING-releases`, solo con releases y sin código. Lo
      crea alguien con permisos de administrador de la organización (confirmar con Carlos).
- [ ] **Token para publicar.** Un token de grano fino con permiso de escritura de *contents* solo sobre
      `CQEING-releases`, guardado como secret `TOKEN_RELEASES`. En `publicar.yml` y `publicar-gestor.yml`:
      `GH_TOKEN: ${{ secrets.TOKEN_RELEASES }}` y `gh release create … --repo utilidadcqeing-prog/CQEING-releases`.
- [ ] **Cambiar el nombre del repo** a `utilidadcqeing-prog/CQEING-releases` en `Repositorio.Nombre` y en
      `LanzadorGestor.EnlaceDescarga` (Hallazgo 6).
- [ ] **Publicar el gestor** con `gestor-vX.Y.Z` en el repo actual, para que los gestores ya instalados lo encuentren,
      y también en `CQEING-releases`. Después, cada add-in con su etiqueta.
- [ ] **Cerrar.** Cuando los equipos aprobados tengan todo actualizado, pasar este repo a **privado** y borrar del repo
      público las releases viejas, que no traen licencia.
- [ ] **Avisar a Carlos** de lo que no tiene arreglo: una copia vieja que nunca se actualice sigue funcionando.

## Cómo probarlo

```
dotnet test gestor/CQEING.Gestor.Core.Tests
dotnet test gestor/CQEING.Gestor.Tests
dotnet test addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests
dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2027
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2027
```

Después, la lista de la Tarea 7.

## Estado

- [ ] Plan revisado con el usuario.
- [ ] Tarea 1: hoja, claves y script en la cuenta del ingeniero Carlos; comprobación previa hecha.
- [x] Tarea 2 (#191), 2026-10-28: `CodigoEquipo.cs` y `Licencia.cs` en `addins/compartido`, enlazados en el núcleo.
      Pruebas: 32 nuevas en verde, entre ellas la firma real de Apps Script con «ñ». Siguen en verde
      `CQEING.Gestor.Core.Tests` (199), `CQEING.Gestor.Tests` (28) y `CQEING.Vigas.Calculo.Tests` (1051). Los 5
      proyectos de `addins/` compilan para 2026 y 2027 sin avisos nuevos.
- [x] Tarea 3 (#192), 2026-10-28: `ServicioLicencias.cs` y `RenovadorLicencia.cs` en el núcleo, con
      `Apoyo/FirmaPrueba.cs` compartida con `LicenciaTests`. 26 pruebas nuevas; `CQEING.Gestor.Core.Tests` (225) y
      `CQEING.Gestor.Tests` (28) en verde, y `GestorRevit` compila para 2026 y 2027. Prueba real contra el servicio
      desde .NET: `Revocada` (Hallazgo 12).
- [x] Tarea 4 (#193), 2026-10-28: `if (!Licencia.Vigente(out _)) return Result.Succeeded;` al comienzo de `OnStartup`
      en `AppVigas.cs`, `armado-columnas/App.cs` (también esconde los botones de diagnóstico de Debug),
      `AppImagenes.cs` y `cuadro-cantidades/App/RevitApplication.cs` (con su `using CQEING.Compartido;`). En el cuadro
      de cantidades, salir antes no rompe `OnShutdown`: `DocumentLifecycleService.Shutdown`, `UiExceptionGuard.Shutdown`
      y `ModelessWindowService.Close` ya comprueban si se iniciaron. Prueba de vigilancia
      `TodosLosAddinsExigenLicenciaTests`: falló con los 4 archivos antes del cambio y pasa después. Núcleo (226) y
      cálculo de vigas (1051) en verde; los 4 add-ins compilan para 2026 y 2027 sin avisos en los archivos tocados.
- [x] Tarea 5 (#194), 2026-10-28: `AvisoLicencia.cs` (aviso y renovación en segundo plano), `VentanaLicencia.cs`
      (Decisión 12) y `RegistroSoporte.cs`, llamado desde `AppGestorRevit.OnStartup`. `VentanaAviso.Boton` pasó a
      `internal` para reutilizarlo. `GestorRevit` compila para 2026 y 2027 sin avisos; núcleo (226) y gestor (28) en
      verde. El comportamiento se prueba en Revit en la Tarea 7.
- [x] Tarea 6 (#195), 2026-10-28, rama `licencias-gestor-195` (sale de `seguridad-licencias`):
  - [x] `VistaGestor` recibe `RenovadorLicencia` (después de `reinicio`) y suma `TextoLicencia`, `CodigoEquipo`,
        `PideNombre`, `NombreSolicitante`, `SolicitarAcceso`, `CopiarCodigo` y `ConsultarLicenciaAsync` (Decisión 13).
        `VentanaPrincipal.xaml` tiene la franja encima del año de Revit y las tarjetas; `VentanaPrincipal.xaml.cs`
        lanza la consulta junto con «Buscar novedades»; `Program.Main` pasa `RenovadorLicencia.Crear(ClienteHttp.Crear())`.
  - [x] Pruebas: `Apoyo/LicenciasFalso.cs` (servicio falso con clave RSA de prueba y código `AAAA-BBBB-CCCC-DDDD`),
        `Entorno` con carpeta de licencia temporal y reloj fijo, y `VistaGestorLicenciaTests` con 9 casos: vigente,
        sin licencia y nombre vacío, pendiente, aprobada con aviso de reinicio, aprobada con licencia previa sin aviso,
        sin conexión sin licencia, sin conexión con licencia vigente, revocada, y que la consulta no frena las tarjetas.
        `CQEING.Gestor.Tests` 37/37 y `CQEING.Gestor.Core.Tests` 226/226 en verde; `CQEING.Gestor` compila.
  - [x] Captura real en `docs/planes/imagenes/2026-10-28-licencia-gestor.png`: el gestor compilado de esta rama
        (`gestor/CQEING.Gestor/bin/Debug/net8.0-windows/CQEING.Gestor.exe`) abierto en el PC del desarrollador, sin
        `licencia.json`. La consulta al abrir fue contra el servicio de prueba real (Hallazgo 11) y respondió `pendiente`:
        la franja muestra «Solicitud enviada. Falta que la aprueben.» con el código del equipo, y las tarjetas cargaron
        sin esperar a la licencia. Sin nombre guardado, la solicitud fue con el usuario de Windows (Tarea 3, paso 1).
        Falta probar «Solicitar acceso», «Copiar» y el paso a `aprobado` en la Tarea 7 (#196).
- [ ] Tarea 7 (#196), 2026-10-28, Revit 2027.3, con los 4 add-ins y `gestor-revit` desplegados desde `licencias-gestor-195`:
  - [x] Sin `licencia.json`: en la pestaña CQEING solo sale el panel «Gestor» y un único aviso con el código del equipo
        (`imagenes/2026-10-28-licencia-revit-sin-licencia.png`). Juan ya había aprobado el equipo, así que el componente
        renovó en segundo plano y dejó una licencia vigente hasta el 31-10-2026.
  - [x] Con la licencia aprobada y Revit reiniciado: salen los 4 paneles y no hay aviso
        (`imagenes/2026-10-28-licencia-revit-con-licencia.png`). El gestor de la rama muestra «Licencia vigente hasta el
        31-10-2026 · PC» (`imagenes/2026-10-28-licencia-gestor-vigente.png`). El botón «Gestor de add-ins» de Revit abre
        el gestor **instalado** (`%LOCALAPPDATA%\CQEING.Gestor\current`), que no tiene la franja hasta que se publique (Tarea 8).
  - [x] Fecha `vence` editada a mano (2026 → 2027): solo sale el panel «Gestor». Al abrir, el componente vio la licencia
        inválida, pidió otra y guardó una buena, emitida a las 20:17 UTC y con su vencimiento real.
  - [x] Sin red, con la licencia vigente: los 4 add-ins cargan. Según los journals, desde que arranca Revit hasta que
        arrancan los add-ins pasan 35 s sin red (`journal.0045`) y 38 s con red (`journal.0044`): la licencia no lo
        demora. Después Revit sí se queda esperando (pausas de 27 s y 22 s), pero son servicios de Autodesk:
        «Get Service Settings Retry … Host desconocido» y «Fail to download revit config from service». No es de CQEING.
  - [x] Revocado: Juan pasó el equipo a `revocado`. Para no esperar las 12 h de la renovación (Decisión 4), se quitó
        `licencia.json` a mano y se abrió Revit con red: solo sale el panel «Gestor» con el aviso. El gestor de la rama
        muestra «El acceso de este equipo fue retirado. Código del equipo: D90A-820C-ACED-F4D9», con «Su nombre» y
        «Solicitar acceso» (`imagenes/2026-10-28-licencia-gestor-revocada.png`). Ojo: con una licencia emitida hace
        menos de 12 h, el `revocado` recién se aplica en la primera renovación después de esas 12 h, y como mucho a los
        3 días, cuando vence.
  - [ ] Copiar `licencia.json` a otro PC (no hay otro PC disponible el 28-oct).
- [ ] Tarea 8: publicado, repo privado, releases viejas borradas.
