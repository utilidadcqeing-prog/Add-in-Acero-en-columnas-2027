# Cómo deben ser los add-ins de CQEING (desde el 29-oct-2026)

Todos los add-ins viven en el repo `utilidadcqeing-prog/Add-in-Acero-en-columnas-2027`, que es **privado**, y se
instalan **solo con el Gestor de add-ins**. Desde ahora cada add-in:

1. **Exige licencia.** Si el equipo no está aprobado en la hoja «Licencias CQEING», el add-in no crea sus botones.
2. **Exige estar al día.** Si hay una versión más nueva publicada, o si el gestor del equipo está desactualizado, el
   add-in no crea sus botones hasta que se actualice desde el gestor.

Las dos reglas ya están programadas en `addins/compartido/`. Lo único que tiene que hacer cada add-in es llamarlas.

---

## 1. Lo obligatorio en el código

### Las dos primeras líneas de `OnStartup`

```csharp
using CQEING.Compartido;

public Result OnStartup(UIControlledApplication app)
{
    // Sin licencia vigente no se crean los botones; el aviso lo da el componente del gestor.
    if (!Licencia.Vigente(out _)) return Result.Succeeded;
    // Con una versión nueva publicada, o el gestor desactualizado, tampoco: hay que actualizar desde el gestor.
    if (!BloqueoPorVersion.AlDia(app)) return Result.Succeeded;

    // ... crear el panel y los botones ...
}
```

- Van **antes** de crear cualquier botón o de iniciar servicios.
- Si el add-in pone sus botones en una pestaña que no es `CQEING`, se le pasa esa pestaña:
  `BloqueoPorVersion.AlDia(app, "MiPestana")`. Así puede desactivarlos si sale una versión nueva con Revit abierto.
- No se lanza nada en `OnStartup`. Si algo falla, el add-in no carga, pero Revit no se cierra.
- La prueba `TodosLosAddinsExigenLicenciaTests` **falla** si un `IExternalApplication` no tiene estas dos líneas. No se
  puede unir un PR sin ellas.

### Los botones

- Se crean con `CintaCqeing.ObtenerPanel(app, "<Panel>")` y `CintaCqeing.CrearBoton(...)`, en la pestaña `CQEING`.
- Cada botón debe ser un `PushButton` cuyo comando esté en la DLL del add-in. Así es como el bloqueo encuentra y
  desactiva sus botones.
- Los botones de diagnóstico o de prueba van dentro de `#if DEBUG`: la versión publicada (Release) no los trae.

### Lo que NO se hace

- No se edita nada de `addins/compartido/Licencia.cs`, `ActualizacionObligatoria.cs` ni `BloqueoPorVersion.cs` para
  un add-in en particular. Si hace falta un cambio, va para todos y con su PR.
- No se distribuyen DLL, ZIP ni `.addin` a mano: lo que no instala el gestor no queda protegido.

---

## 2. La carpeta del add-in

```
addins/<id>/
├── addin.json          ← lo lee el publicador y el gestor
├── <Nombre>.csproj
├── <Nombre>.addin      ← con un ClientId (GUID) propio
├── App.cs              ← el IExternalApplication
└── Resources/          ← iconos <nombre>16.png y <nombre>32.png
```

`addin.json`:

```json
{
  "id": "mi-addin",
  "nombre": "Mi add-in",
  "descripcion": "Qué hace, en una línea, como lo verá el usuario en el gestor.",
  "proyecto": "MiAddin.csproj",
  "revit": ["2026", "2027"],
  "heredados": []
}
```

- `id`: minúsculas y guiones (`acero-vigas`). Es el mismo de las etiquetas y no se cambia nunca.
- `revit`: los años para los que se compila. 2026 usa .NET 8 y 2027 usa .NET 10; eso lo decide
  `addins/Directory.Build.props`, no el `.csproj`.
- `heredados`: los nombres de `.addin` de antes del gestor que hay que borrar al instalar. En un add-in nuevo va vacío.
- Un add-in nuevo se suma a `CQEING-Addins.sln`.

---

## 3. Probar en el equipo

```powershell
dotnet build addins/<id>/<Nombre>.csproj -p:RevitVersion=2026
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin <id> -Revit 2026
dotnet test CQEING-Addins.sln
```

- Revit tiene que estar **cerrado** para desplegar.
- Lo que se despliega con `desplegar-local.ps1` apunta a la carpeta del repo. **Nunca se bloquea por versión**, así que
  sirve para desarrollar. Sí exige licencia: el equipo de desarrollo tiene que estar aprobado en la hoja.
- Antes de pedir el PR: `dotnet test CQEING-Addins.sln` sin fallos y compilación sin advertencias.

---

## 4. Publicar una versión

```powershell
git tag -a <id>-vX.Y.Z -m "Qué cambió en esta versión (lo verá el usuario en el gestor)"
git push origin <id>-vX.Y.Z
```

- La etiqueta va siempre sobre `main`, después de unir el PR.
- Tiene que ser **anotada** (`-a`). Su mensaje son las notas de la versión. Si no es anotada, el workflow falla.
- GitHub compila el add-in para cada año, arma los ZIP y crea la release en el repo público de descargas
  **`utilidadcqeing-prog/CQEING-releases`**. En el repo del código no se crean releases.
- Si se suben varias etiquetas, van **de una en una**: si se suben más de tres juntas, GitHub no lanza los workflows.
- `-rc.N` (por ejemplo `-v1.2.0-rc.1`) publica una prueba que el gestor no muestra.

### Qué número poner

| Cambio | Ejemplo |
|---|---|
| Arreglo pequeño | `1.1.1` → `1.1.2` |
| Algo nuevo para el usuario | `1.1.2` → `1.2.0` |
| Cambio grande o incompatible | `1.2.0` → `2.0.0` |

- El número **siempre sube**. Una versión publicada obliga a todos a actualizar en cuanto el gestor la ve.
- **No se puede volver atrás.** Una versión anterior queda bloqueada mientras exista una más nueva, y el gestor guarda
  en el equipo la versión más alta que conoció. Si una versión sale mal, se arregla publicando otra más nueva.
- Antes de etiquetar, hay que probar en Revit: una versión mala bloquea a todo el mundo hasta que salga la corrección.

### Versiones vigentes (29-oct-2026)

| Add-in | Versión |
|---|---|
| `acero-vigas` | 1.1.1 |
| `armado-columnas` | 1.5.1 |
| `cuadro-cantidades` | 1.9.1 |
| `imagenes` | 1.1.1 |
| Gestor (`gestor-v*`) | 0.3.1 |

---

## 5. Lo que ve el usuario

| Situación al abrir Revit | Qué pasa |
|---|---|
| Equipo sin licencia | No aparece ningún add-in; el gestor muestra el código del equipo y el botón «Solicitar acceso». |
| Un add-in desactualizado | Ese add-in no aparece. El gestor avisa: «Actualizar es obligatorio». |
| Gestor desactualizado | No aparece **ningún** add-in hasta pulsar «Actualizar y reiniciar» en el gestor. |
| Sale una versión con Revit abierto | El botón queda gris con el motivo. La ventana que ya estaba abierta sigue funcionando hasta cerrarla. |
| Sin internet | Funciona con lo último que se supo, y la licencia aguanta 72 h sin conexión. |

En el equipo queda solo la última versión de cada add-in. El gestor borra las anteriores al abrirse y al abrir Revit.

---

## 6. Resumen para el PR

- [ ] `OnStartup` empieza con `Licencia.Vigente` y `BloqueoPorVersion.AlDia`.
- [ ] Botones con `CintaCqeing`; los de diagnóstico, dentro de `#if DEBUG`.
- [ ] `addin.json` completo; si el add-in es nuevo, está sumado a la `.sln`.
- [ ] `dotnet test CQEING-Addins.sln` pasa y la compilación no tiene advertencias.
- [ ] Probado en Revit con `desplegar-local.ps1`.
- [ ] Después de unir: etiqueta anotada `<id>-vX.Y.Z` con un número mayor que el publicado.

Más detalle en `docs/planes/2026-10-28-licencias-hoja-google.md` y `docs/planes/2026-10-29-actualizacion-obligatoria.md`.
