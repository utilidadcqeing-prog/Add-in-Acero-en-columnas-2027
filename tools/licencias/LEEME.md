# Servicio de licencias (hoja «Licencias CQEING»)

Cómo montar, pasar a otra cuenta o rehacer la hoja de Google que aprueba y firma las licencias de los add-ins.
Contexto y decisiones: issue #189 y `docs/planes/2026-10-28-licencias-hoja-google.md`. El código es `servicio.gs`.

## 1. Claves (una sola vez, fuera del repo)

En Git Bash, en una carpeta que **no** esté dentro del repo:

```
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out privada.pem
openssl pkey -in privada.pem -pubout -out publica.pem
tr -d '\r\n' < privada.pem > privada-una-linea.txt
```

- `privada.pem` firma las licencias. Solo va en las propiedades del script, nunca en el repo (`*.pem` está en
  `.gitignore`). Quien la tenga puede emitir licencias.
- `publica.pem` va en `addins/compartido/Licencia.cs` (`ClavePublica`) y en el JSON de la prueba (paso 4).
- `privada-una-linea.txt` es la misma clave en una sola línea, para pegarla en el campo de la propiedad del script.

## 2. Hoja y script

1. Crear una hoja de cálculo en blanco llamada **Licencias CQEING**.
2. *Extensiones → Apps Script*. Borrar lo que trae, pegar `servicio.gs` y guardar.
3. Elegir `configurar` en la lista de funciones y pulsar **Ejecutar**. Google pide permisos (hojas de cálculo y
   correo). Quedan las pestañas `Equipos`, `Registro` y `Config`.
4. *Configuración del proyecto* (engranaje) → *Propiedades del script* → *Agregar propiedad*:
   `CLAVE_PRIVADA` = el contenido de `privada-una-linea.txt`. El script rearma el PEM solo.
5. En `Config`: `B1` = días de vigencia (3). `B2` = correo que recibe las solicitudes. Si `B2` está vacío, se usa el
   correo de la cuenta que implementó el script.

## 3. Publicar como aplicación web

*Implementar → Nueva implementación → tipo: Aplicación web*:
- Ejecutar como: **yo**
- Quién tiene acceso: **cualquier usuario**

Copiar la URL que termina en `/exec`. Esa URL va en `ServicioLicencias.Url`.

La implementación corre con la cuenta de **quien la creó**: los correos salen de esa cuenta y la hoja se edita con
sus permisos. Si la hoja cambia de dueño, el nuevo dueño hace una **nueva implementación** (sección 5).

## 4. Comprobación previa

1. En el editor, ejecutar `probarFirma` y copiar del registro de ejecución el JSON con `datos` y `firma`.
2. Guardar en `gestor/CQEING.Gestor.Core.Tests/Datos/firma-apps-script.json`:
   `{ "clavePublica": "<contenido de publica.pem>", "datos": "…", "firma": "…" }`. Nada de eso es secreto.
3. Abrir en el navegador y luego en PowerShell:
   ```
   Invoke-RestMethod "<url>/exec?codigo=0000-0000-0000-0000&nombre=Prueba&equipo=PC&usuarioWindows=u&version=0"
   ```
   Las dos veces debe salir `estado = pendiente`. En `Equipos` debe aparecer la fila, en `Registro` las consultas,
   y debe llegar el correo de aviso.
4. Borrar la fila de prueba de `Equipos` cuando termine la prueba completa.

## 5. Pasar la hoja a otra cuenta (de Juan al ingeniero Carlos)

1. *Compartir* → agregar a Carlos como editor → junto a su nombre, *Transferir la propiedad*. Carlos acepta desde el
   correo. Solo funciona entre cuentas del mismo tipo (dos Gmail, o dos de la misma empresa en Google Workspace).
   Si no se puede, Carlos usa *Archivo → Hacer una copia* y vuelve a cargar `CLAVE_PRIVADA` (paso 2.4).
2. Carlos abre *Extensiones → Apps Script* y hace una **nueva implementación** (sección 3) con su cuenta. La URL
   nueva reemplaza a la anterior en `ServicioLicencias.Url`.
3. En *Implementar → Gestionar implementaciones*, archivar la implementación anterior.
4. Quitar al dueño anterior como editor.

## Cambiar la clave

Si la clave privada se filtra o se pierde: crear un par nuevo (sección 1) y cambiar `CLAVE_PRIVADA`, `ClavePublica`
y el JSON de la prueba. Después hay que publicar los add-ins otra vez. Las licencias firmadas con la clave vieja
dejan de valer, y los equipos aprobados reciben una nueva en su siguiente conexión.
