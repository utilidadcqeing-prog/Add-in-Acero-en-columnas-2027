# Barra longitudinal continua (#41, #42, #44) -- plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que cada posición longitudinal de una columna sea una sola varilla de la zapata al tope, partida con empalmes nativos de Revit (varios por varilla, reversibles), con el solape del cuadro de la empresa por defecto.

**Architecture:** Dos unidades nuevas en `Logica/`: `TiposEmpalme` (busca o crea el tipo de empalme para una barra) y `BarraContinua` (crea la varilla entera, la parte en una sola llamada, reasigna host, marca y repone ganchos). `GeneradorLongitudinal` conserva el anillo, la planificación de alturas y los estribos menores, y pierde todo el método de dos varillas sueltas y los cortes por cambio de columna.

**Tech Stack:** C# / .NET (net10.0-windows para Revit 2027, también compila contra Revit 2026), Revit API (`Rebar`, `RebarSpliceUtils`, `RebarSpliceTypeUtils`), WPF.

**Spec:** `docs/planes/2026-10-07-diseno-barra-continua-41-42.md`. Mediciones previas: `docs/planes/2026-10-07-factibilidad-barra-continua-41.md`.

## Global Constraints

- Build 2027: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027`. Tiene que dar `0 Errores`.
- Build 2026: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2026 -p:RevitInstallDir=Rev2026\`. Tiene que dar `0 Errores`.
- Vista previa: `dotnet build tools/PreviewUI`. `Modelo/` y `Vistas/` no pueden referenciar `Autodesk.Revit` (salvo `Modelo/EstandarCQ.cs`, que la vista previa excluye).
- Deploy: `powershell -ExecutionPolicy Bypass -File tools/deploy.ps1 -Versions 2027 -TargetFramework net10.0-windows`, **solo** después de comprobar con `Get-Process -Name Revit -ErrorAction SilentlyContinue` que Revit está cerrado.
- **Nunca `git commit` ni `git push` sin pedido explícito de Luciano en ese momento.** Ninguna tarea de este plan termina en commit. Nunca `git add -A`.
- Solo API de Revit pública y documentada en `RevitAPI.xml`. Todo lo que usa este plan ya se verificó ahí (desde 2025): `RebarSpliceUtils.SpliceRebar(Document, ElementId, RebarSpliceOptions, IList<RebarSpliceGeometry>)`, `RebarSpliceGeometry(XYZ, XYZ)`, `RebarSpliceTypeUtils.GetAllRebarSpliceTypes/CreateRebarSpliceType/SetLapLengthMultiplier/SetShiftOption`, `Rebar.SetHostId`.
- No hay pruebas automáticas en el repo. La comprobación de cada tarea es compilar; la de comportamiento es medir en Revit real (tareas 2 y 6). Que compile no alcanza para dar nada por bueno.
- Comentarios y avisos en español, mismo estilo que el código vecino. Respuestas a Luciano en español neutro, sin voseo, en lenguaje llano.
- Pila de referencia en el modelo de prueba: dado 781598 (0,40x0,40, Z -0,65 a 0), columnas 782104 y 782735 (0,30x0,30, Z 0 a 3,5 y 3,5 a 7).

## Review Focus

Sin pruebas automáticas, cada línea queda atada a un paso de medición en Revit.

1. **Corrimiento del empalme hacia afuera.** En la prueba del 7-oct la varilla del lado derecho (X=3,58, centro 3,50) se corrió a 3,593: hacia el borde, contra el estribo. Lo esperable es que siempre se corra hacia el centro. Tarea 2 paso 4 lo mide; tarea 4 lo corrige; tarea 6 lo comprueba.
2. **Columna de una sola pieza o sin zapata.** Tiene que salir igual que hoy: una varilla, sin avisos nuevos. Tarea 6 paso 5.
3. **Tipo de empalme guardado que no existe en este modelo** (configuración guardada traída de otro modelo). Tiene que avisar y usar el por defecto, no fallar. Tarea 6 paso 6.
4. **Columnas de la pila desalineadas o barra que no cabe en la zona común.** Tiene que avisar con medidas y no generar el longitudinal de esa columna, sin tirar excepción ni dejar varillas a medias. Tarea 4 paso 3 (código), tarea 6 paso 7.
5. **Barra fuera del cuadro de la empresa con la opción por defecto.** La varilla queda entera y se avisa una sola vez por columna. Tarea 6 paso 6.

---

### Task 1: `TiposEmpalme` -- resolver el tipo de empalme de una barra

**Files:**
- Create: `Logica/TiposEmpalme.cs`

**Interfaces:**
- Consumes: `EstandarCQ.Buscar(RebarBarType)` → `CuadroVarillas.Varilla?` (con `Numero` y `Traslapo` en metros), `EstandarCQ.APies(double)`.
- Produces:
  - `TiposEmpalme.PrefijoPropio` (`const string`, `"CQ Traslapo #"`).
  - `new TiposEmpalme(Document doc)`.
  - `TiposEmpalme.Resultado Resolver(RebarBarType barra, string? nombreElegido)`.
  - `readonly record struct TiposEmpalme.Resultado(ElementId? Tipo, string? Aviso)`: `Tipo` null = no hay empalme posible; `Aviso` puede venir también junto a un `Tipo` válido.

- [ ] **Step 1: Crear el archivo**

```csharp
namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// Qué tipo de empalme (<c>RebarSpliceType</c>) le toca a una barra
/// longitudinal -- issue #41.
///
/// Por defecto son tipos PROPIOS del add-in, uno por varilla del cuadro de la
/// empresa ("CQ Traslapo #4"), para que el solape salga de
/// <see cref="CuadroVarillas.Varilla.Traslapo"/> y no de lo que traiga cargado
/// cada modelo. Revit define el solape como un múltiplo del diámetro, y el
/// cuadro no es un múltiplo fijo (#4 son 59 diámetros, #8 son 67), por eso hace
/// falta un tipo por varilla. Si el usuario eligió un tipo del modelo, se usa
/// ese tal cual, sin tocarlo.
///
/// Una instancia por corrida: cachea para no crear ni reconfigurar el mismo
/// tipo una vez por varilla.
/// </summary>
public sealed class TiposEmpalme
{
    public const string PrefijoPropio = "CQ Traslapo #";

    public readonly record struct Resultado(ElementId? Tipo, string? Aviso);

    private readonly Document _doc;
    private readonly Dictionary<string, ElementId> _porNombre = new();
    private readonly Dictionary<(ElementId Barra, string Nombre), Resultado> _cache = new();

    public TiposEmpalme(Document doc)
    {
        _doc = doc;
        foreach (ElementId id in RebarSpliceTypeUtils.GetAllRebarSpliceTypes(doc))
        {
            string? nombre = doc.GetElement(id)?.Name;
            if (!string.IsNullOrEmpty(nombre)) _porNombre[nombre] = id;
        }
    }

    /// <summary>Debe llamarse dentro de una transacción abierta: puede crear un tipo.</summary>
    public Resultado Resolver(RebarBarType barra, string? nombreElegido)
    {
        var clave = (barra.Id, nombreElegido ?? "");
        if (_cache.TryGetValue(clave, out Resultado cacheado)) return cacheado;

        Resultado resultado;
        if (string.IsNullOrEmpty(nombreElegido))
        {
            resultado = ResolverPropio(barra);
        }
        else if (_porNombre.TryGetValue(nombreElegido, out ElementId? delModelo))
        {
            resultado = new Resultado(delModelo, null);
        }
        else
        {
            Resultado propio = ResolverPropio(barra);
            string aviso = $"el tipo de empalme «{nombreElegido}» no existe en este modelo; se usa el por defecto del add-in.";
            resultado = new Resultado(propio.Tipo, propio.Aviso is null ? aviso : $"{aviso} {propio.Aviso}");
        }

        _cache[clave] = resultado;
        return resultado;
    }

    private Resultado ResolverPropio(RebarBarType barra)
    {
        CuadroVarillas.Varilla? varilla = EstandarCQ.Buscar(barra);
        if (varilla is null)
            return new Resultado(null,
                $"la barra «{barra.Name}» no está en el cuadro de la empresa, así que no hay solape que aplicar: " +
                "sus varillas quedan enteras, sin traslapos. Para traslaparlas, elija un tipo de empalme del modelo.");

        string nombre = $"{PrefijoPropio}{varilla.Numero}";
        try
        {
            if (!_porNombre.TryGetValue(nombre, out ElementId? id))
            {
                id = RebarSpliceTypeUtils.CreateRebarSpliceType(_doc, nombre).Id;
                _porNombre[nombre] = id;
            }

            // Se vuelven a fijar en cada corrida, por si alguien los cambió a
            // mano: el nombre promete el solape del cuadro.
            double multiplicador = EstandarCQ.APies(varilla.Traslapo) / barra.BarNominalDiameter;
            RebarSpliceTypeUtils.SetLapLengthMultiplier(_doc, id, multiplicador);
            RebarSpliceTypeUtils.SetShiftOption(_doc, id, RebarSpliceShiftOption.BarPlane);
            return new Resultado(id, null);
        }
        catch (Exception ex)
        {
            return new Resultado(null,
                $"no se pudo preparar el tipo de empalme «{nombre}» ({GeometriaSeccion.Explicar(ex)}); " +
                $"las varillas «{barra.Name}» quedan enteras, sin traslapos.");
        }
    }
}
```

- [ ] **Step 2: Compilar para 2027 y 2026**

Run: los dos comandos de build de Global Constraints.
Expected: `0 Errores` en los dos.

---

### Task 2: Medir en Revit lo que el diseño todavía supone

Esta tarea no toca el generador. Amplía el comando temporal para medir tres cosas antes de reescribir nada, y **es una compuerta**: si el paso 4 no da lo esperado, se detiene el plan y se vuelve al diseño con Luciano.

**Files:**
- Modify: `ComandoDiagnosticoBarraContinua.cs`

**Interfaces:**
- Consumes: `TiposEmpalme` de la tarea 1.
- Produces: nada que use otra tarea. Produce mediciones, que se anotan en `docs/planes/2026-10-07-factibilidad-barra-continua-41.md`.

- [ ] **Step 1: Que la barra de prueba reciba su normal**

En `CrearYMedir`, agregar un parámetro `XYZ normal` después de `XYZ planta`, y usarlo en lugar de `XYZ.BasisX` en la llamada a `Rebar.CreateFromCurves`. En la línea del reporte "Pedido:", agregar al final `$" Normal ({normal.X:0}, {normal.Y:0}, {normal.Z:0})."`.

- [ ] **Step 2: Que el empalme use el tipo propio del add-in**

En `Empalmar`, agregar un parámetro `RebarBarType barra` después de `Rebar rebar`, y reemplazar el bloque que hoy toma `tipos.First()` (desde `ICollection<ElementId> tipos = ...` hasta la línea `sb.AppendLine($"Tipo de empalme: ...")`) por:

```csharp
        TiposEmpalme.Resultado tipo = new TiposEmpalme(doc).Resolver(barra, null);
        if (tipo.Aviso is not null) sb.AppendLine($"Aviso del tipo de empalme: {tipo.Aviso}");
        if (tipo.Tipo is null)
        {
            sb.AppendLine("No hay tipo de empalme -- no se puede probar.");
            return piezas;
        }

        ElementId tipoId = tipo.Tipo;
        CuadroVarillas.Varilla? varilla = EstandarCQ.Buscar(barra);
        sb.AppendLine(
            $"Tipo de empalme: «{doc.GetElement(tipoId)?.Name}» (id {tipoId.Value}), multiplicador " +
            $"{RebarSpliceTypeUtils.GetLapLengthMultiplier(doc, tipoId):0.###}. Solape esperado por el cuadro: " +
            $"{(varilla is null ? "(barra fuera del cuadro)" : $"{varilla.Traslapo:0.###} m")}.");
```

Agregar `using practicacq.Logica;` y `using practicacq.Modelo;` arriba. `EmpalmarYUnir` recibe el mismo parámetro nuevo `RebarBarType barra` y lo pasa a `Empalmar`.

Después de listar las piezas en `Empalmar` (justo antes de la línea de "Cadena de empalme reconocida"), agregar la medición del solape y del corrimiento:

```csharp
            for (int i = 1; i < piezas.Count; i++)
            {
                if (Extremos(piezas[i - 1]) is not { } abajo || Extremos(piezas[i]) is not { } arriba) continue;
                sb.AppendLine(
                    $"  Empalme {i}: solape medido {M(abajo.Arriba.Z - arriba.Abajo.Z):0.###} m; la pieza de arriba quedó corrida " +
                    $"({M(arriba.Abajo.X - abajo.Abajo.X) * 100:0.##}, {M(arriba.Abajo.Y - abajo.Abajo.Y) * 100:0.##}) cm en (X, Y) respecto de la de abajo.");
            }
```

- [ ] **Step 3: Cuatro varillas con cuatro normales**

En `Execute`, reemplazar el arreglo `esquinas` y todo el contenido del `try` de la transacción por esto (cada esquina con su normal, y las cuatro se empalman):

```csharp
        // Cada varilla con una normal distinta, para medir hacia dónde corre
        // Revit el empalme: la prueba del 7-oct mostró que con BasisX la del
        // lado derecho se corre hacia AFUERA.
        var pruebas = new (string Nombre, XYZ Punto, XYZ Normal)[]
        {
            ("A: izquierda-abajo, normal +X", new XYZ(xMin + inset, yMin + inset, 0), XYZ.BasisX),
            ("B: derecha-abajo, normal -X", new XYZ(xMax - inset, yMin + inset, 0), XYZ.BasisX.Negate()),
            ("C: derecha-arriba, normal -Y", new XYZ(xMax - inset, yMax - inset, 0), XYZ.BasisY.Negate()),
            ("D: izquierda-arriba, normal +Y", new XYZ(xMin + inset, yMax - inset, 0), XYZ.BasisY),
        };
```

```csharp
                Pieza hostAbajo = columnas[0];
                sb.AppendLine($"Centro de la zona común: X={M((xMin + xMax) / 2):0.###} Y={M((yMin + yMax) / 2):0.###}.");

                foreach ((string nombre, XYZ punto, XYZ normal) in pruebas)
                {
                    sb.AppendLine();
                    sb.AppendLine($"=== Varilla {nombre} ===");
                    Rebar? creada = CrearYMedir(doc, hostAbajo, barra, punto, normal, zInicio, zFin, nombre, sb);
                    if (creada is null) continue;
                    Rehostear(doc, Empalmar(doc, creada, barra, zEmpalmes, pila, sb), columnas, sb);
                }
```

`EmpalmarYUnir` queda sin llamar: borrarlo (la reversibilidad ya se midió el 7-oct). Actualizar el `<summary>` de la clase para que diga qué mide ahora: solape del tipo propio y dirección del corrimiento según la normal.

- [ ] **Step 4: Compilar, desplegar y pedirle la corrida a Luciano**

Run: build 2027, comprobar Revit cerrado, deploy.

Pedirle a Luciano, en lenguaje llano: abrir Revit, botón "Diagnóstico barra continua", clic en las tres columnas de la pila de referencia, Finalizar, pegar el texto y deshacer con Ctrl+Z.

Expected en el reporte:
- **Solape**: `solape medido 0,75 m` con barra #4 (el tipo `CQ Traslapo #4`, multiplicador ≈ 59,055).
- **Corrimiento hacia el centro en las cuatro**: A corre en +X, B en -X, C en -Y, D en +Y. Es decir, el corrimiento sigue el sentido de la normal.
- `AVISOS/ERRORES DE REVIT AL CONFIRMAR: (ninguno)`.

**Compuerta:** si el corrimiento NO sigue el sentido de la normal (por ejemplo B sigue corriendo en +X), detenerse. La tarea 4 depende de eso; hay que volver al diseño con Luciano antes de seguir.

- [ ] **Step 5: Anotar las mediciones**

Agregar al final de `docs/planes/2026-10-07-factibilidad-barra-continua-41.md` una sección `## Segunda medición (tipos propios y sentido del corrimiento)` con los números reales del reporte: solape medido, multiplicador, y el corrimiento (X, Y) de cada una de las cuatro varillas.

---

### Task 3: `BarraContinua` -- crear, partir, reasignar, marcar

**Files:**
- Create: `Logica/BarraContinua.cs`

**Interfaces:**
- Consumes: `MarcaEstribos.Marcar(Element, ElementId)`, `GeometriaSeccion.Explicar(Exception)`, `GeometriaSeccion.Metros(double)`.
- Produces (todas las longitudes en **pies**, unidades internas de Revit):
  - `readonly record struct BarraContinua.ColumnaDePila(FamilyInstance Host, double ZInf, double ZSup)`.
  - `sealed record BarraContinua.Pedido(RebarBarType Barra, double X, double Y, XYZ Normal, double ZInicio, double ZFin, IReadOnlyList<double> ZTraslapos, IReadOnlyList<ColumnaDePila> Pila, RebarHookType? GanchoAbajo, RebarHookType? GanchoArriba, RebarTerminationOrientation OrientacionAbajo, RebarTerminationOrientation OrientacionArriba, ElementId? TipoEmpalme)`. `Pila` ordenada de abajo hacia arriba y no vacía; `ZTraslapos` en orden ascendente.
  - `readonly record struct BarraContinua.Resultado(int Piezas, string? Aviso)`.
  - `static BarraContinua.Resultado BarraContinua.Crear(Document doc, Pedido p)`.

- [ ] **Step 1: Crear el archivo**

```csharp
namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

/// <summary>
/// Una posición longitudinal de la columna como UNA sola varilla de punta a
/// punta, partida después con empalmes nativos de Revit -- issue #41.
///
/// Todo lo que hace esto se midió contra Revit real el 7-oct (ver
/// docs/planes/2026-10-07-factibilidad-barra-continua-41.md):
///  - un Rebar hosteado en una sola columna puede recorrer toda la pila, sin
///    recortarse ni dar aviso;
///  - <see cref="RebarSpliceUtils.SpliceRebar(Document, ElementId, RebarSpliceOptions, IList{RebarSpliceGeometry})"/>
///    acepta varios empalmes en una llamada, también fuera del host;
///  - después de partir, Revit deja piezas hosteadas en una columna que no las
///    contiene ("completely outside of its host"): hay que reasignarlas;
///  - el corrimiento lateral lo pone Revit, en zigzag, en el sentido de la
///    normal de la barra.
///
/// Las longitudes van todas en pies (unidades internas de Revit).
/// </summary>
public static class BarraContinua
{
    public readonly record struct ColumnaDePila(FamilyInstance Host, double ZInf, double ZSup);

    public sealed record Pedido(
        RebarBarType Barra, double X, double Y, XYZ Normal, double ZInicio, double ZFin,
        IReadOnlyList<double> ZTraslapos, IReadOnlyList<ColumnaDePila> Pila,
        RebarHookType? GanchoAbajo, RebarHookType? GanchoArriba,
        RebarTerminationOrientation OrientacionAbajo, RebarTerminationOrientation OrientacionArriba,
        ElementId? TipoEmpalme);

    /// <summary><c>Piezas</c> = cuántos Rebar quedaron en el modelo (0 si no se pudo crear la varilla).</summary>
    public readonly record struct Resultado(int Piezas, string? Aviso);

    public static Resultado Crear(Document doc, Pedido p)
    {
        Rebar? entera;
        try
        {
            entera = CrearEntera(doc, p);
        }
        catch (Exception ex)
        {
            // Una creación fallida dentro de la transacción puede arrastrar a
            // las siguientes -- mismo criterio que GeneradorArmado.GenerarColumna.
            try { doc.Regenerate(); } catch { /* best-effort */ }
            return new Resultado(0, $"no se pudo crear una barra longitudinal ({GeometriaSeccion.Explicar(ex)}).");
        }

        if (entera is null)
            return new Resultado(0,
                $"Revit no pudo crear una barra longitudinal «{p.Barra.Name}» (Rebar.CreateFromCurves devolvió null) en " +
                $"X={GeometriaSeccion.Metros(p.X):0.###} Y={GeometriaSeccion.Metros(p.Y):0.###}, " +
                $"Z {GeometriaSeccion.Metros(p.ZInicio):0.###} a {GeometriaSeccion.Metros(p.ZFin):0.###} m.");

        MarcaEstribos.Marcar(entera, entera.GetHostId());

        if (p.ZTraslapos.Count == 0 || p.TipoEmpalme is null)
            return new Resultado(1, null);

        List<Rebar> piezas;
        try
        {
            piezas = Partir(doc, entera, p);
        }
        catch (Exception ex)
        {
            try { doc.Regenerate(); } catch { /* best-effort */ }
            return new Resultado(1,
                $"Revit no pudo aplicar los traslapos de una varilla «{p.Barra.Name}» ({GeometriaSeccion.Explicar(ex)}); " +
                "quedó entera, sin traslapos.");
        }

        if (piezas.Count == 0)
            return new Resultado(1,
                $"Revit no partió una varilla «{p.Barra.Name}» en sus traslapos; quedó entera, sin traslapos.");

        // SpliceRebar reemplaza la barra por elementos nuevos: ni la marca ni
        // (sin confirmar) las terminaciones de la original sobreviven.
        foreach (Rebar pieza in piezas)
        {
            Reasignar(doc, pieza, p.Pila);
            MarcaEstribos.Marcar(pieza, pieza.GetHostId());
        }

        ReponerGancho(piezas[0], 0, p.GanchoAbajo, p.OrientacionAbajo);
        ReponerGancho(piezas[^1], 1, p.GanchoArriba, p.OrientacionArriba);

        int esperadas = p.ZTraslapos.Count + 1;
        return new Resultado(piezas.Count, piezas.Count == esperadas
            ? null
            : $"una varilla «{p.Barra.Name}» quedó en {piezas.Count} pieza(s) y se esperaban {esperadas}: " +
              "Revit no aplicó todos los traslapos pedidos.");
    }

    private static Rebar? CrearEntera(Document doc, Pedido p)
    {
        // ZInicio < ZFin siempre -- Start del Line es el extremo INFERIOR, así
        // que el extremo 0 de la barra es el de abajo.
        Line linea = Line.CreateBound(new XYZ(p.X, p.Y, p.ZInicio), new XYZ(p.X, p.Y, p.ZFin));

        using var terminaciones = new BarTerminationsData(doc);
        if (p.GanchoAbajo is not null)
        {
            terminaciones.HookTypeIdAtStart = p.GanchoAbajo.Id;
            terminaciones.TerminationOrientationAtStart = p.OrientacionAbajo;
        }

        if (p.GanchoArriba is not null)
        {
            terminaciones.HookTypeIdAtEnd = p.GanchoArriba.Id;
            terminaciones.TerminationOrientationAtEnd = p.OrientacionArriba;
        }

        // El host es la columna de más abajo de la pila aunque la varilla la
        // exceda por las dos puntas (medido: Revit no la recorta). La normal
        // tiene que ser perpendicular a la barra: con BasisZ el plano queda
        // degenerado y CreateFromCurves devuelve null siempre.
        return Rebar.CreateFromCurves(
            doc, RebarStyle.Standard, p.Barra, p.Pila[0].Host, p.Normal,
            new List<Curve> { linea }, terminaciones,
            useExistingShapeIfPossible: true, createNewShape: true);
    }

    /// <summary>Las piezas resultantes, de abajo hacia arriba. Vacío si Revit no devolvió ninguna.</summary>
    private static List<Rebar> Partir(Document doc, Rebar entera, Pedido p)
    {
        doc.Regenerate();

        // La X/Y se lee de la barra ya creada, no del pedido: el punto de
        // empalme tiene que tocar la barra exacto, si no Revit acepta los
        // datos pero no corta (aviso del Dynamo de referencia de William).
        (XYZ abajo, XYZ _) = Extremos(entera);

        var opciones = new RebarSpliceOptions(doc, p.TipoEmpalme!, RebarSplicePosition.Middle);
        List<RebarSpliceGeometry> geometrias = p.ZTraslapos
            .Select(z => new RebarSpliceGeometry(new XYZ(abajo.X, abajo.Y, z), XYZ.BasisZ))
            .ToList();

        IList<ElementId> resultantes = RebarSpliceUtils.SpliceRebar(doc, entera.Id, opciones, geometrias);
        doc.Regenerate();

        return resultantes
            .Select(id => doc.GetElement(id) as Rebar)
            .Where(r => r is not null)
            .Select(r => r!)
            .OrderBy(r => Extremos(r).Abajo.Z)
            .ToList();
    }

    /// <summary>Pasa la pieza a la columna de la pila que contiene su punto medio, si no es ya la suya.</summary>
    private static void Reasignar(Document doc, Rebar pieza, IReadOnlyList<ColumnaDePila> pila)
    {
        (XYZ abajo, XYZ arriba) = Extremos(pieza);
        double zMedio = (abajo.Z + arriba.Z) / 2;

        foreach (ColumnaDePila columna in pila)
        {
            if (zMedio < columna.ZInf || zMedio > columna.ZSup) continue;
            if (columna.Host.Id == pieza.GetHostId()) return;

            // Best-effort: si Revit no acepta el cambio, la pieza queda con el
            // host que le puso SpliceRebar -- sigue siendo una columna de la
            // pila, así que el borrado al regenerar la encuentra igual.
            try { pieza.SetHostId(doc, columna.Host.Id); } catch { /* best-effort */ }
            return;
        }
    }

    private static void ReponerGancho(
        Rebar pieza, int extremo, RebarHookType? gancho, RebarTerminationOrientation orientacion)
    {
        if (gancho is null) return;

        try { pieza.SetHookTypeId(extremo, gancho.Id); } catch { /* best-effort */ }
        try { pieza.SetTerminationOrientation(extremo, orientacion); } catch { /* best-effort */ }
    }

    /// <summary>Extremos inferior y superior de la línea central, sin ganchos ni radios.</summary>
    private static (XYZ Abajo, XYZ Arriba) Extremos(Rebar rebar)
    {
        List<XYZ> puntos = rebar
            .GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0)
            .SelectMany(c => new[] { c.GetEndPoint(0), c.GetEndPoint(1) })
            .OrderBy(q => q.Z)
            .ToList();

        return (puntos[0], puntos[^1]);
    }
}
```

- [ ] **Step 2: Compilar para 2027 y 2026**

Run: los dos comandos de build.
Expected: `0 Errores` en los dos.

---

### Task 4: `GeneradorLongitudinal` -- zona común, una varilla por posición, fuera el método viejo

**Files:**
- Modify: `Logica/GeneradorLongitudinal.cs` (cabecera 10-58; `Generar` 66-107; `GenerarColumna` 111-461; `AgruparPorHost` 746-768; borrar 770-1090 salvo lo indicado; `PlanificarCortes` 1092-1157; borrar `ReponerGancho` y `CrearBarraVertical` 1274-1417)
- Modify: `Logica/GeometriaSeccion.cs` (borrar `BuscarTipoEmpalme`, ~215-235, con su `<summary>`)
- Modify: `Modelo/ConfigLongitudinal.cs`

Los números de línea son de antes de este cambio; ubicar cada bloque por el nombre del método.

**Interfaces:**
- Consumes: `TiposEmpalme` (tarea 1), `BarraContinua` (tarea 3). El sentido del corrimiento medido en la tarea 2.
- Produces: `ConfigLongitudinal.TipoEmpalmeNombre` (`string?`, null = por defecto del add-in), que usa la tarea 5.

- [ ] **Step 1: Campo nuevo en `ConfigLongitudinal`**

Debajo de la propiedad `LongitudComercialBarra`:

```csharp
    /// <summary>
    /// Tipo de empalme de Revit (<c>RebarSpliceType</c>, por nombre) con el que
    /// se parten las barras longitudinales en sus traslapos -- issue #41. Vacío
    /// = el por defecto del add-in: un tipo propio por varilla, con el solape
    /// del cuadro de la empresa (<see cref="CuadroVarillas.Varilla.Traslapo"/>).
    /// </summary>
    public string? TipoEmpalmeNombre { get; set; }
```

Y en `Copiar()`, debajo de `LongitudComercialBarra = LongitudComercialBarra,`:

```csharp
        TipoEmpalmeNombre = TipoEmpalmeNombre,
```

- [ ] **Step 2: `Generar` -- un `TiposEmpalme` por corrida**

Reemplazar el comentario "Empalmes reales (...)" y la línea `ElementId? tipoEmpalme = GeometriaSeccion.BuscarTipoEmpalme(doc);` por:

```csharp
        // Un solo resolvedor por corrida: cachea los tipos de empalme propios
        // del add-in para no crearlos ni reconfigurarlos una vez por varilla.
        var tiposEmpalme = new TiposEmpalme(doc);
```

En la llamada a `GenerarColumna` y en su firma, reemplazar el parámetro `ElementId? tipoEmpalme` por `TiposEmpalme tiposEmpalme` (mismo lugar en la lista). Borrar `GeometriaSeccion.BuscarTipoEmpalme` con su `<summary>`.

- [ ] **Step 3: `GenerarColumna` -- pila y zona común (#42, #44)**

Reemplazar todo el tramo que va desde el comentario `// Un tramo de barra por elemento host de la pila (...)` (antes de `porHost = AgruparPorHost(contexto)`) hasta el final del bloque `if (contexto.IdZapata is not null && zInicioTotal < porHost[0].ZInf - Tolerancia) { ... }` inclusive, por:

```csharp
        // La longitudinal es de la columna física completa (William, 9-sep):
        // recorre TODOS los tramos, aceptados o no. Lo aceptado en la UI es
        // solo para configurar estribos.
        List<(string IdHost, double ZInf, double ZSup)> porHost = AgruparPorHost(contexto);

        // #44: un tramo sin columna del modelo detrás ya no corta la varilla
        // (es una sola de punta a punta), pero tampoco se calla.
        foreach (Tramo sinHost in contexto.Tramos.Where(t => t.IdElementoHost is null))
            res.Avisos.Add(
                $"{contexto.Marca}: el tramo de {sinHost.ZInferior:0.##} a {sinHost.ZSuperior:0.##} m no tiene ninguna " +
                "columna del modelo asociada; la barra longitudinal lo atraviesa igual, pero conviene revisar el modelo ahí.");

        // La pila: las columnas apiladas que existen y admiten armadura, de
        // abajo hacia arriba. La varilla se hostea en la primera y, una vez
        // partida, cada pieza pasa a la que la contiene (ver BarraContinua).
        //
        // La posición de las varillas sale de la ZONA COMÚN en planta de todas
        // ellas -- la intersección de sus huellas, o sea la sección más chica,
        // esté donde esté (#42). Antes salía de la columna de más arriba,
        // suponiendo que el ensanche siempre está en la base. Con una sola
        // varilla recta para toda la pila, tiene que caber en todas.
        var pila = new List<BarraContinua.ColumnaDePila>();
        double xMin = double.MinValue, xMax = double.MaxValue;
        double yMin = double.MinValue, yMax = double.MaxValue;
        foreach ((string idHostTexto, double zInf, double zSup) in porHost)
        {
            if (!long.TryParse(idHostTexto, out long idHostLong) ||
                doc.GetElement(new ElementId(idHostLong)) is not FamilyInstance host)
            {
                res.Avisos.Add(
                    $"{contexto.Marca}: el elemento {idHostTexto} ya no existe; la barra longitudinal atraviesa ese tramo igual.");
                continue;
            }

            if (RebarHostData.GetRebarHostData(host) is null)
            {
                res.Avisos.Add(
                    $"{contexto.Marca}: el elemento {idHostTexto} no admite armadura; la barra longitudinal atraviesa ese " +
                    "tramo igual, hosteada en otra columna de la pila.");
                continue;
            }

            (CurveLoop contornoCrudo, double _) = GeometriaSeccion.ObtenerContornoHost(host, cacheHost);
            (double hXMin, double hXMax, double hYMin, double hYMax) = GeometriaSeccion.Huella(contornoCrudo);
            xMin = Math.Max(xMin, hXMin);
            xMax = Math.Min(xMax, hXMax);
            yMin = Math.Max(yMin, hYMin);
            yMax = Math.Min(yMax, hYMax);

            pila.Add(new BarraContinua.ColumnaDePila(host, EstandarCQ.APies(zInf), EstandarCQ.APies(zSup)));
        }

        if (pila.Count == 0)
        {
            res.Avisos.Add($"{contexto.Marca}: ninguna columna de la pila admite armadura; se omite el acero longitudinal.");
            return;
        }

        string zonaComun =
            $"{GeometriaSeccion.Metros(Math.Max(0, xMax - xMin)) * 100:0.#}×{GeometriaSeccion.Metros(Math.Max(0, yMax - yMin)) * 100:0.#} cm";

        // Si la sección común es muy chica para el recubrimiento + la barra,
        // el inset cruza el medio de la sección y CreateFromCurves devuelve
        // null sin decir por qué (probado sobre Revit real). Incluye el caso
        // de columnas desalineadas, donde la zona común sale vacía.
        if ((xMax - xMin) - 2 * insetEsquinas <= Tolerancia || (yMax - yMin) - 2 * insetEsquinas <= Tolerancia)
        {
            res.Avisos.Add(
                $"{contexto.Marca}: la zona común a todas las columnas de la pila ({zonaComun}) es demasiado chica para el " +
                $"recubrimiento + la barra de esquina «{barraEsquinas.Name}»; se omite el acero longitudinal de esta columna.");
            return;
        }

        if (barraCaraX is not null && (xMax - xMin) - 2 * insetX <= Tolerancia)
            res.Avisos.Add(
                $"{contexto.Marca}: la zona común de la pila ({zonaComun}) es demasiado chica para la barra de cara X " +
                $"«{barraCaraX.Name}»; sus posiciones pueden quedar mal.");

        if (barraCaraY is not null && (yMax - yMin) - 2 * insetY <= Tolerancia)
            res.Avisos.Add(
                $"{contexto.Marca}: la zona común de la pila ({zonaComun}) es demasiado chica para la barra de cara Y " +
                $"«{barraCaraY.Name}»; sus posiciones pueden quedar mal.");

        var referencia = new FootprintHost(pila[0].Host, xMin, xMax, yMin, yMax);

        // El longitudinal entra a anclarse hasta 7.5cm de la cara inferior de
        // la zapata (#26). Ya no hace falta hostear ese pedazo en la zapata:
        // la varilla puede salirse de su host (medido el 7-oct).
        double zInicioTotal = contexto.ZInferiorLongitudinal;
```

Notas para ubicar el reemplazo: el bloque borrado incluye el bucle `foreach` que llenaba `footprintPorHost`, el cálculo de `referenciaNullable`/`referencia`, y el alta de la zapata en `porHost`. Las líneas de `columnas`, `filas`, `insetX`, `insetY` (que hoy están justo antes) se conservan y tienen que quedar **antes** de este bloque, porque lo usan.

- [ ] **Step 4: `GenerarColumna` -- el bucle de creación**

Reemplazar `double zFinTotal = porHost[^1].ZSup;` por:

```csharp
        double zFinTotal = contexto.ZSuperior;
```

Borrar el comentario "El plan de cortes (...)" con `var cortesPorBarraYGrupo = ...`, todo el comentario largo "Corrimiento lateral en cada traslapo REAL (...)" y la función local `Corrimiento`.

Reemplazar desde `var anillo = new List<...>();` hasta el final del `for` que llama a `CrearBarraConTraslapos` y el `if (creadas > 0)` que le sigue (el comentario largo "Las posiciones longitudinales EN ORDEN DE ANILLO" que está justo arriba se conserva; el `int creadas = 0;` de más arriba se borra) por:

```csharp
        var anillo = new List<(RebarBarType Barra, double X, double Y, XYZ Normal)>();

        double xEsqIzq = referencia.XMin + insetEsquinas;
        double xEsqDer = referencia.XMax - insetEsquinas;
        double yEsqAbajo = referencia.YMin + insetEsquinas;
        double yEsqArriba = referencia.YMax - insetEsquinas;
        double xCentro = (referencia.XMin + referencia.XMax) / 2;
        double yCentro = (referencia.YMin + referencia.YMax) / 2;

        // La normal de la barra decide dos cosas: el plano donde dobla el
        // gancho, y hacia dónde corre Revit la pieza de arriba de cada empalme
        // (a lo largo de la normal, en su mismo sentido -- medido el 7-oct, ver
        // docs/planes/2026-10-07-factibilidad-barra-continua-41.md). Por eso:
        //  - el EJE es el que va a lo largo de la cara donde apoya la barra,
        //    para que las dos piezas del empalme queden pegadas al borde y no
        //    una metida hacia adentro del hormigón (dibujo de Luciano, 9-sep);
        //  - el SENTIDO es hacia el centro de la sección, para que la pieza
        //    corrida no se vaya contra el estribo.
        // Las esquinas apoyan en dos caras: se dejan en X, como estaban.
        XYZ NormalEnX(double x) => x <= xCentro ? XYZ.BasisX : XYZ.BasisX.Negate();
        XYZ NormalEnY(double y) => y <= yCentro ? XYZ.BasisY : XYZ.BasisY.Negate();

        void AgregarEsquina(double x, double y) =>
            anillo.Add((barraEsquinas, x, y, NormalEnX(x)));

        // Intermedias de las caras inferior/superior: varían en X.
        void AgregarDeCaraX(int c, double y)
        {
            if (barraCaraX is null) return;
            double x = PosicionIntermedia(xEsqIzq, xEsqDer, columnas, c);
            anillo.Add((barraCaraX, x, y, NormalEnX(x)));
        }

        // Intermedias de las caras izquierda/derecha: varían en Y.
        void AgregarDeCaraY(int f, double x)
        {
            if (barraCaraY is null) return;
            double y = PosicionIntermedia(yEsqAbajo, yEsqArriba, filas, f);
            anillo.Add((barraCaraY, x, y, NormalEnY(y)));
        }

        AgregarEsquina(xEsqIzq, yEsqAbajo);
        for (int c = 0; c < columnas; c++) AgregarDeCaraX(c, referencia.YMin + insetX);
        AgregarEsquina(xEsqDer, yEsqAbajo);
        for (int f = 0; f < filas; f++) AgregarDeCaraY(f, referencia.XMax - insetY);
        AgregarEsquina(xEsqDer, yEsqArriba);
        for (int c = columnas - 1; c >= 0; c--) AgregarDeCaraX(c, referencia.YMax - insetX);
        AgregarEsquina(xEsqIzq, yEsqArriba);
        for (int f = filas - 1; f >= 0; f--) AgregarDeCaraY(f, referencia.XMin + insetY);

        // En un anillo CERRADO la alternancia estricta solo existe si la
        // cantidad de posiciones es par. Con el modelo de hoy siempre lo es
        // (4 + 2*columnas + 2*filas), pero no se deja implícito.
        if (anillo.Count % 2 != 0)
            res.Avisos.Add(
                $"{contexto.Marca}: la sección tiene {anillo.Count} barras longitudinales (cantidad impar), " +
                "así que dos barras vecinas van a traslapar a la misma altura -- con un número impar " +
                "alrededor del perímetro no hay forma de alternarlas todas.");

        // Las alturas de traslapo dependen solo del grupo par/impar: se
        // calculan (y avisan) una vez por grupo, no una vez por varilla.
        var alturasPorGrupo = new Dictionary<bool, List<double>>();

        // Los avisos de empalme son los mismos para todas las varillas de un
        // mismo tipo de barra: uno por columna y motivo, no uno por varilla.
        var avisosDeBarras = new HashSet<string>();

        int piezas = 0;
        for (int i = 0; i < anillo.Count; i++)
        {
            (RebarBarType barraDeLaPosicion, double x, double y, XYZ normal) = anillo[i];

            bool esParGrupo = i % 2 == 0;
            if (!alturasPorGrupo.TryGetValue(esParGrupo, out List<double>? alturas))
            {
                alturas = PlanificarAlturas(
                    esParGrupo, niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra, contexto.Marca, res);
                alturasPorGrupo[esParGrupo] = alturas;
            }

            // El tipo de empalme solo se resuelve (y, si es propio, se crea)
            // cuando esta varilla de verdad lleva algún traslapo.
            ElementId? tipoEmpalme = null;
            if (alturas.Count > 0)
            {
                TiposEmpalme.Resultado tipo = tiposEmpalme.Resolver(barraDeLaPosicion, cfg.TipoEmpalmeNombre);
                tipoEmpalme = tipo.Tipo;
                if (tipo.Aviso is not null) avisosDeBarras.Add(tipo.Aviso);
            }

            (RebarTerminationOrientation orientacionAbajo, RebarTerminationOrientation orientacionArriba) =
                OrientacionesHaciaAdentro(referencia, x, y, normal);

            BarraContinua.Resultado creada = BarraContinua.Crear(doc, new BarraContinua.Pedido(
                barraDeLaPosicion, x, y, normal,
                EstandarCQ.APies(zInicioTotal), EstandarCQ.APies(zFinTotal),
                alturas.Select(EstandarCQ.APies).ToList(), pila,
                ganchoLongitudinal, ganchoLongitudinal, orientacionAbajo, orientacionArriba,
                tipoEmpalme));

            piezas += creada.Piezas;
            if (creada.Aviso is not null) avisosDeBarras.Add(creada.Aviso);
        }

        foreach (string aviso in avisosDeBarras)
            res.Avisos.Add($"{contexto.Marca}: {aviso}");

        if (piezas > 0)
            res.Detalles.Add(
                $"{contexto.Marca}: {anillo.Count} barra(s) longitudinal(es) de punta a punta, {piezas} pieza(s) con sus traslapos.");
```

`ganchoLongitudinal` se pasa en las dos puntas: hoy solo se resuelve cuando hay zapata (bloque de más arriba, sin cambios), así que sin zapata es null en las dos, igual que antes.

- [ ] **Step 5: `PlanificarCortes` pasa a `PlanificarAlturas`**

Reemplazar el método `PlanificarCortes` entero (con su `<summary>`) por:

```csharp
    /// <summary>
    /// Las alturas (en metros) donde traslapa una varilla de un grupo par o
    /// impar: solo donde la longitud comercial de la barra
    /// (<paramref name="longitudComercialMetros"/>) obliga a cortar, y solo a
    /// la mitad de un "nivel" (issue #7), alternando niveles pares/impares
    /// según <paramref name="esParGrupo"/> para que dos barras vecinas nunca
    /// traslapen a la misma altura.
    ///
    /// Ya no hay cortes obligados por límite de columna: una misma varilla
    /// puede recorrer toda la pila (issue #41, medido contra Revit real el
    /// 7-oct). No depende del tipo de barra ni de su posición X/Y, por eso
    /// <see cref="GenerarColumna"/> lo calcula una vez por grupo.
    /// </summary>
    private static List<double> PlanificarAlturas(
        bool esParGrupo,
        List<(int Ordinal, double ZInf, double ZSup)> niveles,
        double zInicioTotalMetros,
        double zFinTotalMetros,
        double longitudComercialMetros,
        string marcaColumna,
        ResultadoArmado res)
    {
        List<double> candidatos = niveles
            .Where(n => (n.Ordinal % 2 == 0) == esParGrupo)
            .Select(n => RedondearArriba10Cm((n.ZInf + n.ZSup) / 2))
            .Where(z => z > zInicioTotalMetros + Tolerancia && z < zFinTotalMetros - Tolerancia)
            .OrderBy(z => z)
            .ToList();

        return PlanificarSplicesReales(
            candidatos, zInicioTotalMetros, zFinTotalMetros, longitudComercialMetros, marcaColumna, res);
    }
```

`PlanificarSplicesReales` no se toca.

- [ ] **Step 6: Borrar el método viejo**

Borrar de `GeneradorLongitudinal.cs`, cada uno con su `<summary>`:
- el record `PlanDeCortes`;
- `CrearBarraConTraslapos` (incluye las funciones locales `CrearRangoConMetodoAnterior` y todo el bucle `while (s0 < totalSegmentos)`);
- `IntentarPartirConSplice`;
- `ReponerGancho`;
- `CrearBarraVertical`.

Se conservan: `FootprintHost`, `AgruparPorHost`, `PlanificarSplicesReales`, `RedondearArriba10Cm`, `PosicionIntermedia`, `OrientacionesHaciaAdentro`, `OrientacionPositiva`/`OrientacionNegativa`, y todo lo de grapas y cajas.

Actualizar el `<summary>` de `AgruparPorHost`: reemplazar "-- un Rebar longitudinal por host, no por tramo (corre continuo dentro de un mismo elemento aunque tenga un nudo de viga en el medio)" por "-- son las columnas apiladas que la varilla atraviesa, y a las que se reasigna cada pieza después de partirla".

- [ ] **Step 7: Reescribir el primer punto del comentario de cabecera**

En el `<summary>` de la clase, reemplazar el punto "Fase 5 (#7, traslapos): implementada en ..." entero (hasta "...quien exige partir el objeto ahí.") por:

```csharp
///  - Fase 5 (#7, traslapos) e issue #41: cada posición longitudinal es UNA
///    sola varilla de la zapata al tope, creada por <see cref="BarraContinua"/>
///    y partida después con empalmes nativos de Revit. Un Rebar hosteado en
///    una columna SÍ puede recorrer toda la pila -- medido contra Revit real
///    el 7-oct (docs/planes/2026-10-07-factibilidad-barra-continua-41.md); la
///    suposición contraria, que obligaba a cortar en cada cambio de columna,
///    era falsa. Los traslapos solo se generan donde la longitud comercial de
///    la barra (<see cref="ConfigLongitudinal.LongitudComercialBarra"/>, 6 o
///    12 m) obliga a cortar, y solo a la mitad de un nivel, alternando niveles
///    pares/impares entre las posiciones de la sección. Qué barra cae en cada
///    grupo lo decide su lugar en el ANILLO del perímetro (ver
///    <see cref="GenerarColumna"/>, lista <c>anillo</c>): "uno sí, uno no"
///    dando la vuelta, de modo que dos barras VECINAS nunca traslapan a la
///    misma altura -- regla del equipo, confirmada por Luciano el 11-sep (ver
///    docs/planes/2026-09-11-alternancia-de-traslapos-por-anillo.md). El
///    solape y el corrimiento lateral los pone Revit según el tipo de empalme
///    (<see cref="TiposEmpalme"/>): por defecto uno propio del add-in con el
///    solape del cuadro de la empresa.
```

- [ ] **Step 8: Compilar para 2027 y 2026**

Run: los dos comandos de build.
Expected: `0 Errores` en los dos. Si quedan advertencias de variable o `using` sin usar introducidas por este cambio, quitarlas.

Run: `grep -n "CrearBarraConTraslapos\|IntentarPartirConSplice\|CrearBarraVertical\|PlanDeCortes\|BuscarTipoEmpalme\|footprintPorHost" Logica/*.cs`
Expected: sin resultados.

---

### Task 5: Interfaz -- elegir el tipo de empalme

**Files:**
- Modify: `Modelo/IProveedorTipos.cs`
- Modify: `Modelo/ProveedorTiposFalso.cs`
- Modify: `Logica/ProveedorTiposReal.cs`
- Modify: `Vistas/VentanaConfiguradorEstribos.xaml` (junto a `ComboLongitudBarra`, ~línea 299)
- Modify: `Vistas/VentanaConfiguradorEstribos.xaml.cs` (~212, ~1020, ~1034)

**Interfaces:**
- Consumes: `ConfigLongitudinal.TipoEmpalmeNombre` (tarea 4), `TiposEmpalme.PrefijoPropio` (tarea 1).
- Produces: `IProveedorTipos.ListarEmpalmes()` → `IReadOnlyList<string>`.

- [ ] **Step 1: `IProveedorTipos`**

Dentro de la interfaz, después de `ListarGanchos()`:

```csharp

    /// <summary>
    /// Los tipos de empalme del modelo, por nombre -- para el combo "Tipo de
    /// empalme" de la longitudinal (issue #41). No incluye los propios del
    /// add-in ("CQ Traslapo #N"): esos son la opción por defecto, que la
    /// ventana ofrece aparte. Puede venir vacía.
    /// </summary>
    IReadOnlyList<string> ListarEmpalmes();
```

- [ ] **Step 2: `ProveedorTiposFalso`**

Al final de la clase:

```csharp

    public IReadOnlyList<string> ListarEmpalmes() => new List<string>
    {
        "Empalme solapado 40D",
        "Empalme solapado 60D",
    };
```

- [ ] **Step 3: `ProveedorTiposReal`**

Al final de la clase:

```csharp

    /// <summary>
    /// Sin los tipos propios del add-in: cada uno lleva el solape de UNA
    /// varilla del cuadro, y elegirlo a mano se lo aplicaría a todas.
    /// </summary>
    public IReadOnlyList<string> ListarEmpalmes() =>
        RebarSpliceTypeUtils.GetAllRebarSpliceTypes(_doc)
            .Select(id => _doc.GetElement(id)?.Name)
            .Where(n => !string.IsNullOrEmpty(n) && !n.StartsWith(TiposEmpalme.PrefijoPropio))
            .Select(n => n!)
            .OrderBy(n => n)
            .ToList();
```

- [ ] **Step 4: El combo en el XAML**

Debajo de la línea `<ComboBox x:Name="ComboLongitudBarra" Height="24" />`:

```xml

                    <TextBlock Text="Tipo de empalme (solape de los traslapos):" Style="{StaticResource Etiqueta}" />
                    <ComboBox x:Name="ComboTipoEmpalme" Height="24" />
```

- [ ] **Step 5: El code-behind**

Como campo de la clase `VentanaConfiguradorEstribos`, junto a las demás constantes privadas:

```csharp
    /// <summary>
    /// Primera opción del combo de tipo de empalme: los tipos propios del
    /// add-in, con el solape del cuadro de la empresa. Se guarda como
    /// <c>null</c> en <see cref="ConfigLongitudinal.TipoEmpalmeNombre"/>.
    /// </summary>
    private const string EmpalmePorDefecto = "Por defecto del add-in (cuadro CQ)";
```

Debajo de `ComboLongitudBarra.SelectedItem = 12.0;`:

```csharp

        ComboTipoEmpalme.ItemsSource = new[] { EmpalmePorDefecto }.Concat(_tipos.ListarEmpalmes()).ToList();
        ComboTipoEmpalme.SelectedItem = EmpalmePorDefecto;
```

En `LeerLongitudinalDelPanel`, debajo de la línea de `LongitudComercialBarra`:

```csharp
        TipoEmpalmeNombre = ComboTipoEmpalme.SelectedItem is string empalme && empalme != EmpalmePorDefecto ? empalme : null,
```

En `MostrarLongitudinalEnPanel`, debajo de `ComboLongitudBarra.SelectedItem = cfg.LongitudComercialBarra;`:

```csharp
        // Un nombre que este modelo no tiene (configuración guardada traída de
        // otro) se muestra como el por defecto; el generador avisa igual.
        ComboTipoEmpalme.SelectedItem =
            cfg.TipoEmpalmeNombre is not null && ComboTipoEmpalme.Items.Contains(cfg.TipoEmpalmeNombre)
                ? cfg.TipoEmpalmeNombre
                : EmpalmePorDefecto;
```

`ConfigLongitudinalPorDefecto` no se toca: `TipoEmpalmeNombre` queda en null, que es el por defecto. Verificar que el archivo ya tenga `using System.Linq;`; si no, agregarlo.

- [ ] **Step 6: Compilar todo**

Run: build 2027, build 2026 y `dotnet build tools/PreviewUI`.
Expected: `0 Errores` en los tres.

- [ ] **Step 7: Ver el combo en la vista previa**

Run: `dotnet run --project tools/PreviewUI`
Expected: en el panel de la longitudinal aparece "Tipo de empalme (solape de los traslapos):" con "Por defecto del add-in (cuadro CQ)" seleccionado y, al desplegar, "Empalme solapado 40D" y "Empalme solapado 60D". Elegir uno, cambiar de columna y volver: la elección se conserva.

---

### Task 6: Medir en Revit real, documentar y limpiar

**Files:**
- Create: `docs/planes/2026-10-07-barra-continua-implementada.md`
- Modify (solo al final, con todo confirmado): `ComandoDiagnosticoBarraContinua.cs` (borrar), `App.cs` (quitar el botón)

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: el registro de lo medido y el cierre de los issues.

- [ ] **Step 1: Desplegar**

Run: `Get-Process -Name Revit -ErrorAction SilentlyContinue` (tiene que no devolver nada), build 2027, deploy.
Luego comprobar que el `.dll` instalado en `%APPDATA%\Autodesk\Revit\Addins\2027\practicacq.dll` referencia `RevitAPI, Version=27.x` con `[Reflection.Assembly]::ReflectionOnlyLoadFrom(...).GetReferencedAssemblies()`.

- [ ] **Step 2: Corrida principal (pedírsela a Luciano en lenguaje llano)**

Sobre la pila de referencia: abrir el configurador, aceptar al menos un tramo, longitud comercial **6 m** (para forzar traslapos en una pila de 7,65 m), tipo de empalme por defecto, casilla "Borrar TODO el armado existente" tildada, generar. Después, botón "Diagnóstico de Rebar" sobre **todas** las piezas de una varilla de esquina y de una varilla de cara lateral, y pegar el texto.

Expected:
- Ningún corte en Z=0 ni en Z=3,5 (los cambios de columna): las piezas solo se cortan en los traslapos.
- Solape de 0,75 m entre piezas consecutivas de barra #4.
- Cada pieza hosteada en la columna que la contiene.
- La pieza de arriba de cada traslapo corrida un diámetro **hacia el centro** de la sección, y a lo largo de su cara (Review Focus 1).
- Gancho abajo en la pieza de más abajo, hacia adentro (el reporte dice "ADENTRO").
- En el resumen del add-in, ningún aviso de traslapo; en Revit, ningún aviso de "completely outside of its host".

- [ ] **Step 3: Regenerar**

Generar otra vez la misma columna con la casilla de borrar tildada.
Expected: la misma cantidad de piezas que antes, sin duplicados. Contarlas seleccionando la columna en 3D.

- [ ] **Step 4: Deshacer un empalme a mano**

En Revit, seleccionar un empalme de una varilla generada y quitarlo con la herramienta nativa.
Expected: las dos piezas vuelven a ser una sola varilla.

- [ ] **Step 5: Casos simples (Review Focus 2)**

Generar sobre una columna de una sola pieza con zapata y sobre una sin zapata, longitud comercial 12 m.
Expected: una varilla por posición, sin traslapos, sin avisos nuevos. Sin zapata, sin gancho (igual que hoy).

- [ ] **Step 6: Tipos de empalme (Review Focus 3 y 5)**

1. Elegir "Empalme solapado 60D" en el combo y generar con 6 m: el solape medido tiene que ser 0,762 m (60 × 12,7 mm), no 0,75.
2. Guardar una configuración con ese tipo, borrar o renombrar el tipo en Revit, aplicar la configuración guardada y generar: aviso "el tipo de empalme «...» no existe en este modelo; se usa el por defecto del add-in", y solape de 0,75 m.
3. Si el modelo tiene una barra fuera del cuadro (#46), usarla en las esquinas con 6 m: las varillas quedan enteras y aparece **un** aviso por columna.

- [ ] **Step 7: Pila que no encaja (Review Focus 4)**

Si el modelo tiene (o se puede armar moviendo una columna de prueba) una pila con columnas desalineadas en planta: generar.
Expected: aviso "la zona común a todas las columnas de la pila (... cm) es demasiado chica ...", ninguna varilla longitudinal en esa columna, los estribos sí se generan, sin excepción. Deshacer el movimiento con Ctrl+Z.

- [ ] **Step 8: Documentar**

Crear `docs/planes/2026-10-07-barra-continua-implementada.md` con: contexto (una línea, enlazando el diseño y la factibilidad), qué se cambió (archivos nuevos, qué se borró del generador), los números reales medidos en los pasos 2 a 7, y una sección "Pendiente" con lo que no se haya podido probar. Si algún paso dio distinto de lo esperado, no se da por cerrado: se anota y se le plantea a Luciano.

- [ ] **Step 9: Limpiar y cerrar (solo con todo confirmado por Luciano)**

- Borrar `ComandoDiagnosticoBarraContinua.cs` y el bloque `// TEMPORAL -- ver comentario de ComandoDiagnosticoBarraContinua.cs.` con su `panelHerramientas.AddItem(...)` en `App.cs`. Build 2027 y 2026.
- Proponerle a Luciano (no hacerlo sin que lo pida): commit, y comentar/cerrar #41 y #42, y actualizar #44 con que el hueco ya no se produce y ahora se avisa.
