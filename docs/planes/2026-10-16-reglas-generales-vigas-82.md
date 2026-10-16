# Plan: reglas generales de vigas (#82)

> **Para agentes:** SUB-SKILL REQUERIDA: superpowers:subagent-driven-development (recomendada) o superpowers:executing-plans, tarea por tarea. Los pasos usan casillas (`- [ ]`).

**Objetivo:** que las reglas generales del motor de vigas (múltiplo de corte, varilla, distancia libre y la tabla por diámetro) se guarden en un archivo del PC, valgan para todos los proyectos, queden bloqueadas y solo cambien al desbloquearlas.

**Arquitectura:** todo vive en la librería sin Revit (`CQEING.Vigas.Calculo`). `Reglas` gana los ganchos que faltan y una validación. `AlmacenReglas` lee y escribe el JSON. `EdicionReglas` maneja el bloqueo y el borrador, que es lo que pinta el panel. La pantalla es de #83: solo enlaza con `EdicionReglas`.

**Tecnología:** C# `net8.0`, `System.Text.Json` (incluido en .NET), xUnit.

**Fuente:** la issue [#82](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/82), `Desktop\addin vigas\traslapos-vigas-notas.md` (§0 "Reglas y datos" y §10), los dos cuadros de Carlos (imágenes del 16-oct) y la maqueta, vista "Reglas" (https://claude.ai/artifact/3BrJfHRe2rEGmG4ztFxhFh, `project/Main.dc.html`, `estado="reglas"`).

## Contexto

- **Referencia única:** la maqueta, opción 1. Nada del add-in de columnas, ni su código ni su interfaz (Juan, 2026-10-16).
- **Lo que pinta la maqueta** (panel derecho, botón "Reglas"):
  - "Reglas generales · Son la base para todos los proyectos";
  - la franja del candado: "Bloqueadas para evitar cambios por error." con **Desbloquear**, o "Desbloqueadas: los cambios valen para todos los proyectos." con **Guardar y bloquear**;
  - Múltiplo de corte (m), Distancia libre entre barras (cm), Varilla 6 m | 12 m ("Si un tramo no se puede resolver con 6 m, se usa una de 12 m.");
  - la tabla del **#3 al #8** con Traslapo, Gancho 90°, Estribo 135° y kg/m.
- **Ya existe** (de #80 y #88, `Modelo/Reglas.cs`): `Reglas.Generales` con el traslapo, el gancho de 90° (como `int`), los kg/m y el diámetro del #2 al #8, más el múltiplo 500, la varilla de 6000, la de 12000 y la distancia libre de 25 mm. El aviso `SinTraslapoMinimo` ya está en `MotorVigas.cs:17`.
- **Rama:** `vigas-reglas-82`, que sale de `vigas-portico-81`; las ramas de #80, #88 y #81 siguen apiladas sin fusionar.
- **Fuera de alcance:**
  - la pantalla y la conversión de texto a número con sus unidades (m, cm): #83;
  - dibujar los ganchos en Revit con E, D y A: #85;
  - el peso del #4: pendiente en #79. Queda en 0.994.

## Decisiones (Juan, 2026-10-16)

1. **El recubrimiento no es una regla general.** Va en el panel de Armado, como en la maqueta, y ya entra con cada viga (`Seccion.Recubrimiento`). Se quita de la #82.
2. **El #2 se guarda, pero no se muestra.** La pantalla muestra del #3 al #8 (`Reglas.DiametrosEnPantalla`), igual que el gancho de 180°.
3. **Si alguien sale con cambios sin guardar, el add-in avisa.** `EdicionReglas.HayCambiosSinGuardar` lo dice y #83 muestra el aviso.
   - Texto: "Hay cambios sin guardar en las reglas generales."
   - Botones: **Guardar y bloquear** · **Descartar** · **Seguir editando**.
   - La maqueta no tiene este estado: se agrega cuando se haga #83.
4. **Las reglas se guardan en un archivo del PC:** `%AppData%\CQEING\vigas\reglas.json`, con la misma carpeta base (`%AppData%\CQEING\<add-in>`) que usa el monorepo. Valen para todos los proyectos de ese usuario de Windows en ese equipo. Si el archivo no existe, se usan los cuadros de Carlos.
5. **E, D y A en mm enteros.** Los cuadros los dan en cm con un decimal (7.7 cm = 77 mm). B ya estaba en mm.
6. **Al abrir el panel, las reglas siempre están bloqueadas.** El bloqueo no se guarda en el archivo.

## Restricciones generales

- Todas las medidas en mm, como el resto del motor; los pesos en kg/m.
- Nombres, mensajes y comentarios en español, como el código actual.
- Si falla la lectura del archivo, Revit no se cae: se usan los valores base y se informa.
- `dotnet test CQEING-Addins.sln -c Release` en verde al terminar cada tarea.

## Lo que más puede fallar y ninguna prueba "normal" toca

1. **Archivo dañado o a medio escribir** (Revit se cerró al guardar): se usa la base y `ProblemaAlCargar` dice qué pasó. Prueba en la tarea 3.
2. **Archivo viejo al que le falta un diámetro o un campo nuevo:** se completa con la base sin perder lo que sí trae. Prueba en la tarea 3.
3. **La carpeta no existe la primera vez:** se crea al guardar. Prueba en la tarea 3.
4. **El usuario cambia un valor y lo vuelve a dejar como estaba:** no hay cambios sin guardar, porque la comparación es por valor y no por referencia. Prueba en la tarea 4.
5. **"Guardar y bloquear" con un 0 o un negativo:** no guarda, sigue desbloqueado y devuelve el mensaje. Prueba en la tarea 4.

---

### Tarea 1: ganchos de 180° y estribo de 135° en las reglas

**Archivos:**
- Modificar: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo/Modelo/Reglas.cs`
- Modificar: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo/MotorVigas.cs:94` (`regla.Gancho90` → `regla.Gancho90.B`)
- Prueba: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests/ReglasTests.cs`

**Interfaces:**
- Produce:
  - `public sealed record Gancho(int B, int? E = null, int? D = null, int? A = null);` (mm; B = longitud del gancho, E = extensión recta, D = diámetro de doblado, A = longitud recta de anclaje)
  - `public sealed record ReglaDiametro(int? Traslapo, Gancho Gancho90, Gancho Gancho180, Gancho Estribo135, double KgPorMetro, double Diametro);`
  - `public static IReadOnlyList<int> Reglas.DiametrosEnPantalla` = `[3, 4, 5, 6, 7, 8]`
  - `public bool Reglas.Equivale(Reglas otra)`: mismos escalares, mismas claves y cada `ReglaDiametro` igual por valor.

- [ ] **Paso 1: pruebas que fallan**, en `ReglasTests`:
  - `ElGanchoDe90DelNumero5EsElDelCuadroDeDimensiones`: `Gancho90 == new Gancho(278, 191, 95, 227)`.
  - `ElGanchoDe180DelNumero8EsElDelCuadroDeDimensiones`: `Gancho180 == new Gancho(381, 102, 152, 319)`.
  - `ElEstriboDe135DelNumero3SaleDelCuadroDeDimensiones`: `Estribo135 == new Gancho(113, 57, 38, 84)` (no 0.131 del cuadro de traslapos).
  - `ElEstriboDe135DelNumero6SaleDelCuadroDeTraslaposSinEDA`: `Estribo135 == new Gancho(272)`.
  - `ElNumero2NoTieneTraslapoYNoSeMuestra`: `Traslapo` null y `DiametrosEnPantalla` sin el 2.
  - `DosReglasConLosMismosValoresEquivalen`: `Reglas.Generales.Equivale(Reglas.Generales with { PorDiametro = new Dictionary<int, ReglaDiametro>(Reglas.Generales.PorDiametro) })` es true, y con `Modulo = 600` es false.
- [ ] **Paso 2:** `dotnet test addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests --filter ReglasTests`. Debe fallar porque `Gancho` no existe.
- [ ] **Paso 3:** implementar en `Reglas.cs`. Valores de `Reglas.Generales` en mm (B, E, D, A):

  | # | Gancho 90° | Gancho 180° | Estribo 135° | kg/m | Diámetro |
  |---|---|---|---|---|---|
  | 2 | 112, 77, 38, 86 | 96, 65, 38, 110 | 76, 38, 26, 57 | 0.250 | 6.4 |
  | 3 | 166, 114, 57, 128 | 142, 65, 57, 131 | 113, 57, 38, 84 | 0.560 | 9.5 |
  | 4 | 222, 152, 76, 181 | 190, 65, 76, 160 | 151, 76, 51, 113 | 0.994 | 12.7 |
  | 5 | 278, 191, 95, 227 | 238, 65, 95, 200 | 189, 95, 64, 141 | 1.552 | 15.9 |
  | 6 | 334, 229, 115, 272 | 286, 76, 115, 240 | 272 | 2.235 | 19.1 |
  | 7 | 388, 266, 133, 317 | 333, 89, 133, 279 | 316 | 3.042 | 22.2 |
  | 8 | 444, 305, 152, 363 | 381, 102, 152, 319 | 362 | 3.973 | 25.4 |

  Los traslapos no cambian (#2 null, 550, 750, 900, 1100, 1350, 1700). En el resumen de `Estribo135`, anotar que del #6 al #8 el cuadro de dimensiones no trae E, D ni A.
- [ ] **Paso 4:** `dotnet test CQEING-Addins.sln -c Release`. Todo en verde: las pruebas del motor siguen igual porque B no cambió.
- [ ] **Paso 5:** commit `feat(vigas): ganchos de 180° y estribo de 135° en las reglas (#82)`.

### Tarea 2: validación de las reglas

**Archivos:**
- Modificar: `.../Modelo/Reglas.cs`
- Prueba: `.../CQEING.Vigas.Calculo.Tests/ReglasTests.cs`

**Interfaces:**
- Produce: `public IReadOnlyList<string> Reglas.Validar()`. Vacía = válidas. Un mensaje por problema, en este orden: escalares y después diámetros de menor a mayor.

Mensajes exactos:
- `Modulo <= 0`: "El múltiplo de corte debe ser mayor que 0."
- `Varilla` distinta de 6000 y de 12000: "La varilla debe ser de 6 m o de 12 m."
- `DistanciaLibre <= 0`: "La distancia libre entre barras debe ser mayor que 0."
- `Traslapo <= 0` (null se permite; el motor ya avisa): "El traslapo del #5 debe ser mayor que 0."
- `Gancho90.B <= 0`: "El gancho de 90° del #5 debe ser mayor que 0." Lo mismo con "El gancho de 180°" y "El estribo de 135°".
- `KgPorMetro <= 0`: "El peso por metro del #5 debe ser mayor que 0."

- [ ] **Paso 1: pruebas que fallan:**
  - `LasReglasGeneralesSonValidas`: `Validar()` vacía.
  - `UnMultiploEnCeroNoEsValido`: `with { Modulo = 0 }` → `["El múltiplo de corte debe ser mayor que 0."]`.
  - `UnaVarillaDe9MetrosNoEsValida`: `Varilla = 9000` → `["La varilla debe ser de 6 m o de 12 m."]`.
  - `UnTraslapoNegativoDelNumero5NoEsValido`: el #5 con `Traslapo = -1` → `["El traslapo del #5 debe ser mayor que 0."]`.
  - `UnDiametroSinTraslapoSiEsValido`: el #2 con traslapo null → vacía.
- [ ] **Paso 2:** ejecutar el filtro `ReglasTests` → FALLA.
- [ ] **Paso 3:** implementar `Validar()`.
- [ ] **Paso 4:** `dotnet test CQEING-Addins.sln -c Release` → en verde.
- [ ] **Paso 5:** commit `feat(vigas): validar las reglas generales (#82)`.

### Tarea 3: guardar y leer las reglas en el PC

**Archivos:**
- Crear: `.../CQEING.Vigas.Calculo/Modelo/AlmacenReglas.cs`
- Prueba: `.../CQEING.Vigas.Calculo.Tests/AlmacenReglasTests.cs`

**Interfaces:**
- Consume: `Reglas`, `ReglaDiametro`, `Gancho` (tarea 1).
- Produce:
  - `public sealed class AlmacenReglas(string ruta)`
  - `public static string AlmacenReglas.RutaPorDefecto` = `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CQEING", "vigas", "reglas.json")`
  - `public ReglasCargadas Cargar()`
  - `public void Guardar(Reglas reglas)`
  - `public sealed record ReglasCargadas(Reglas Reglas, string? Problema);`

Formato: JSON con `"version": 1` y los campos de `Reglas` en camelCase. Las claves de los diámetros son el número de la barra (`"5"`). El archivo se puede leer y editar a mano.

Reglas de `Cargar()`:
- El archivo no existe → `Reglas.Generales` y `Problema` null.
- No se puede leer o el JSON está dañado → `Reglas.Generales` y `Problema` = "No se pudieron leer las reglas guardadas; se usan las de base." Nunca lanza.
- Le falta un diámetro de la base → se toma de `Reglas.Generales`. Los diámetros que trae se respetan.

`Guardar()` crea la carpeta si falta. Escribe en `<ruta>.tmp` y después hace `File.Move(tmp, ruta, overwrite: true)`, para que un cierre a mitad no deje el archivo cortado. Si se guardan dos a la vez desde dos Revit, queda el último.

- [ ] **Paso 1: pruebas que fallan.** Cada una usa una carpeta temporal nueva (`Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))`) y la borra al final:
  - `SinArchivo_SeUsanLasReglasGenerales`: `Cargar().Reglas.Equivale(Reglas.Generales)` y `Problema` null.
  - `LoQueSeGuardaSeVuelveALeer`: guardar `Generales with { Modulo = 600, Varilla = 12000 }` y cargar → `Equivale` true.
  - `GuardarCreaLaCarpetaSiNoExiste`: la ruta en una subcarpeta que no existe → `File.Exists` true.
  - `UnArchivoDañado_NoRevientaYAvisa`: escribir `"{ no es json"` → `Generales` y `Problema` con el texto de arriba.
  - `UnArchivoSinUnDiametro_LoCompletaConLaBase`: guardar, quitar `"8"` del JSON a mano, cargar → `PorDiametro[8] == Generales.PorDiametro[8]` y el `Modulo` guardado se conserva.
  - `NoQuedaElArchivoTemporal`: después de `Guardar`, `File.Exists(ruta + ".tmp")` false.
  - `LaRutaPorDefectoEstaEnAppDataCqeingVigas`: termina en `Path.Combine("CQEING", "vigas", "reglas.json")`.
- [ ] **Paso 2:** `--filter AlmacenReglasTests` → FALLA.
- [ ] **Paso 3:** implementar `AlmacenReglas`.
- [ ] **Paso 4:** `dotnet test CQEING-Addins.sln -c Release` → en verde.
- [ ] **Paso 5:** commit `feat(vigas): guardar las reglas generales en el PC (#82)`.

### Tarea 4: bloqueo y edición de las reglas

**Archivos:**
- Crear: `.../CQEING.Vigas.Calculo/Modelo/EdicionReglas.cs`
- Prueba: `.../CQEING.Vigas.Calculo.Tests/EdicionReglasTests.cs`

**Interfaces:**
- Consume: `AlmacenReglas`, `ReglasCargadas` (tarea 3), `Reglas.Validar()` (tarea 2), `Reglas.Equivale()` (tarea 1).
- Produce (lo que enlaza #83):
  - `public sealed class EdicionReglas(AlmacenReglas almacen)`: carga al construirse.
  - `Reglas Guardadas { get; }`, `Reglas Borrador { get; }`, `bool Bloqueadas { get; }` (true al empezar), `string? ProblemaAlCargar { get; }`
  - `bool HayCambiosSinGuardar => !Borrador.Equivale(Guardadas)`
  - `void Desbloquear()`
  - `void Cambiar(Reglas nuevas)` y `void CambiarDiametro(int diametro, ReglaDiametro regla)`: lanzan `InvalidOperationException` si están bloqueadas.
  - `IReadOnlyList<string> GuardarYBloquear()`: valida. Con errores, no guarda, sigue desbloqueada y los devuelve. Sin errores, guarda, `Guardadas = Borrador`, bloquea y devuelve una lista vacía.
  - `void Descartar()`: `Borrador = Guardadas` y bloquea.

En #83, "Volver al pórtico" hace lo siguiente: si `HayCambiosSinGuardar`, muestra el aviso de la decisión 3; si no, llama a `Descartar()`.

- [ ] **Paso 1: pruebas que fallan** (almacén en una carpeta temporal):
  - `AlAbrir_EstanBloqueadasYSinCambios`.
  - `BloqueadasNoSePuedenCambiar`: `Cambiar(...)` lanza `InvalidOperationException`.
  - `DesbloqueadasSeCambianYHayCambiosSinGuardar`: `Desbloquear(); Cambiar(Borrador with { Modulo = 600 })` → `HayCambiosSinGuardar` true y `Guardadas.Modulo` sigue en 500.
  - `SiSeVuelveAlValorOriginal_NoHayCambios`: cambiar a 600 y después a 500 → false.
  - `GuardarYBloquear_GuardaParaTodosLosProyectos`: después de guardar, un `EdicionReglas` nuevo sobre la misma ruta lee `Modulo` 600 y `Bloqueadas` true.
  - `GuardarConUnValorInvalido_NoGuardaYSigueDesbloqueada`: `Modulo = 0` → devuelve el mensaje de la tarea 2, `Bloqueadas` false y el archivo no existe.
  - `Descartar_VuelveALoGuardadoYBloquea`.
  - `CambiarDiametro_SoloTocaEseDiametro`: cambiar el traslapo del #5 a 1000 → el #5 cambia y el #4 no.
  - `ConArchivoDañado_ElProblemaLlegaALaEdicion`: `ProblemaAlCargar` no es null.
- [ ] **Paso 2:** `--filter EdicionReglasTests` → FALLA.
- [ ] **Paso 3:** implementar `EdicionReglas`.
- [ ] **Paso 4:** `dotnet test CQEING-Addins.sln -c Release` → en verde.
- [ ] **Paso 5:** commit `feat(vigas): bloqueo y edición de las reglas generales (#82)`.

### Tarea 5: documentación

**Archivos:**
- Modificar: `Desktop\addin vigas\traslapos-vigas-notas.md`:
  - línea 49: "editable por proyecto" pasa a "editable en las reglas generales (base para todos los proyectos)";
  - §0 "Reglas y datos": agregar las decisiones 1 a 4 de este plan.
- Issue #82, **solo con el visto bueno de Juan**:
  - quitar el recubrimiento de la tabla;
  - agregar las decisiones (el #2 oculto, el aviso al salir y el archivo en `%AppData%\CQEING\vigas\reglas.json`);
  - anotar que no hay E, D ni A para estribos del #6 al #8.

- [ ] **Paso 1:** editar las notas.
- [ ] **Paso 2:** proponerle a Juan el texto nuevo de la issue y actualizarla cuando lo apruebe.
- [ ] **Paso 3:** marcar los criterios de aceptación de abajo con su prueba.

## Criterios de aceptación

Cada criterio cumplido lleva entre paréntesis la prueba que lo comprueba.

- [x] Valores por defecto: múltiplo 0.50 m, varilla de 6 m con 12 m automática, distancia libre de 2.5 cm y la tabla de Carlos (`ReglasTests`, `SinArchivo_SeUsanLasReglasGenerales`).
- [x] Ganchos de 90° y 180° y estribo de 135° por diámetro, con E, D y A cuando el cuadro los trae (`ElGanchoDe90DelNumero5EsElDelCuadroDeDimensiones`, `ElGanchoDe180DelNumero8EsElDelCuadroDeDimensiones`, `ElEstriboDe135DelNumero3SaleDelCuadroDeDimensiones`, `ElEstriboDe135DelNumero6SaleDelCuadroDeTraslaposSinEDA`).
- [x] Peso por metro por diámetro; el #4 queda en 0.994 hasta que se cierre #79 (`UnPesoEnCeroDelNumero4NoEsValido`, `CapaCalculada.KgPorMetro`).
- [x] Base para todos los proyectos: se guarda en el PC y se vuelve a leer (`LoQueSeGuardaSeVuelveALeer`, `GuardarYBloquear_GuardaParaTodosLosProyectos`, `LaRutaPorDefectoEstaEnAppDataCqeingVigas`).
- [x] Bloqueadas por defecto; para cambiarlas hay que desbloquear; al guardar quedan bloqueadas (`AlAbrir_EstanBloqueadasYSinCambios`, `BloqueadasNoSePuedenCambiar`, `GuardarYBloquear_GuardaParaTodosLosProyectos`).
- [x] Aviso si se sale con cambios sin guardar: `HayCambiosSinGuardar` ya está y la pantalla lo muestra en #83 (`DesbloqueadasSeCambianYHayCambiosSinGuardar`, `SiSeVuelveAlValorOriginal_NoHayCambios`).
- [x] Un diámetro sin traslapo mínimo se avisa antes de calcular (ya existía: `AvisosTests`).
- [x] `dotnet test CQEING-Addins.sln -c Release` en verde: 34 + 8 + 109.
