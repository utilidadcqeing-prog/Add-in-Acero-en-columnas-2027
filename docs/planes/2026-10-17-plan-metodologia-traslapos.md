# Nueva metodología de traslapos -- plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que los traslapos de la barra longitudinal nunca toquen un nudo, que cada varilla mida un múltiplo del redondeo elegido sin pasarse del largo comercial, que el solape del cuadro sea un mínimo, y que la ventana dibuje los tipos A y B con todas sus medidas mientras se configura, usando el mismo cálculo que genera.

**Architecture:** Un cálculo puro, sin Revit, en `Modelo/PlanTraslapos.cs` (búsqueda exhaustiva por programación dinámica sobre milímetros enteros), probado con un proyecto de consola propio (`tools/PruebasTraslapos`). La ventana lo llama en vivo y lo dibuja en un control nuevo (`Vistas/PanelTraslapos`). El generador lo llama de nuevo al generar y le pasa a `BarraContinua` la altura y el tipo de empalme de cada traslapo; `TiposEmpalme` crea un tipo por diámetro y largo. Antes de tocar la generación, un comando de diagnóstico mide en Revit real que se pueda partir una varilla con traslapos de largos distintos.

**Tech Stack:** C# / .NET (net10.0-windows para Revit 2027, también compila contra Revit 2026), Revit API (`Rebar`, `RebarSpliceUtils`, `RebarSpliceTypeUtils`), WPF.

**Spec:** `docs/planes/2026-10-17-diseno-metodologia-traslapos.md`. Leerlo entero antes de empezar cualquier tarea.

## Global Constraints

- Build 2027: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027`. Tiene que dar `0 Errores`.
- Build 2026: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2026 -p:RevitInstallDir=Rev2026\`. Tiene que dar `0 Errores`.
- Vista previa: `dotnet build tools/PreviewUI`. `Modelo/` y `Vistas/` no pueden referenciar `Autodesk.Revit` (salvo `Modelo/EstandarCQ.cs`, que la vista previa excluye).
- Pruebas del cálculo: `dotnet run --project tools/PruebasTraslapos`. Tiene que terminar con `0 fallas` y código de salida 0.
- Deploy: `powershell -ExecutionPolicy Bypass -File tools/deploy.ps1 -Versions 2027 -TargetFramework net10.0-windows`, **solo** después de comprobar con `Get-Process -Name Revit -ErrorAction SilentlyContinue` que Revit está cerrado (que no devuelva nada).
- **Nunca `git commit` ni `git push` sin pedido explícito de Luciano en ese momento.** Ninguna tarea de este plan termina en commit. Nunca `git add -A`.
- Solo API de Revit pública y documentada en `C:\Program Files\Autodesk\Revit 2027\RevitAPI.xml`. Ya verificadas para este plan: `RebarSpliceUtils.SpliceRebar(Document, ElementId, RebarSpliceOptions, IList<RebarSpliceGeometry>)`, `RebarSpliceUtils.CanRebarBeSpliced(Rebar, RebarSpliceOptions, RebarSpliceGeometry)`, `RebarSpliceUtils.GetSpliceChain(Rebar)`, `Rebar.TotalLength`, `BuiltInParameter.REBAR_ELEM_LENGTH`. Cualquier otro miembro nuevo se busca ahí antes de usarlo.
- Las medidas del lado `Modelo/`/`Vistas/` van en **metros**; las de Revit, en pies (`EstandarCQ.APies`).
- Reglas de negocio (copiadas del spec): traslapo nunca en un nudo (`TipoTramo.DentroDeViga`); A traslapa en tramos libres de ordinal par, B en los impares (ordinal desde 1, contando `Libre` y `ContraZapata` de abajo hacia arriba); A = posición par del anillo; cada largo de corte (ganchos incluidos) es múltiplo exacto del redondeo; ninguna varilla supera el largo comercial; traslapo ≥ solape mínimo; preferencias en orden: menos traslapos, menos exceso de solape, más centrado, más abajo.
- Redondeos ofrecidos: 0,10 / 0,20 / 0,25 / 0,50 / 1,00 m; por defecto 0,50.
- Comentarios y avisos en español, mismo estilo que el código vecino. Respuestas a Luciano en español neutro, sin voseo, en lenguaje llano.
- Pila de referencia en el modelo de prueba: dado 781598 (0,40x0,40, Z -0,65 a 0), columnas 782104 y 782735 (0,30x0,30, Z 0 a 3,5 y 3,5 a 7).

## Review Focus

1. **Columna sin zapata cuyo primer tramo arranca en la misma cota que la barra** (C-1, C-7 de la vista previa): el traslapo no puede empezar en la punta de abajo de la varilla (la segunda pieza nacería en el mismo punto que la primera). Lo esperable: siempre arranca al menos 1 cm más arriba. Lo cubre la prueba `forzado_un_solo_tramo` (Task 2).
2. **Configuración guardada antes de este cambio** (sin `TipoBarraANombre`): al cargarla no puede quedar el combo A/B vacío ni romperse la generación. Lo esperable: A = esquinas, B = cara X, con aviso. Prueba `migracion_config_vieja` (Task 3) y paso de vista previa (Task 4).
3. **Barra elegida que no está en el cuadro** (un diámetro raro del modelo): la pestaña no puede quedar en blanco ni la ventana tirar excepción. Lo esperable: ese tipo en rojo con el motivo. Prueba `barra_fuera_del_cuadro` (Task 2).
4. **Redondeo fino (0,10 m) en una columna alta**: la pestaña se recalcula en cada cambio de combo; no puede congelar la ventana. Lo esperable: menos de 1 s. Prueba `rendimiento_c12` (Task 2).
5. **Lo dibujado y lo generado no coinciden** (por ejemplo, porque Revit mide el gancho distinto que el cuadro): lo esperable es que el largo que informa Revit sea el de la pestaña. Lo mide el caso 4 del diagnóstico (Task 1) y el paso 2 de Task 8.

---

### Task 1: Diagnóstico en Revit -- traslapos de largos distintos en una misma varilla

Regla del `CLAUDE.md`: no programar la generación sobre una suposición. Esta tarea **no cambia el add-in**: agrega un comando temporal, Luciano lo corre en el modelo de referencia, y el resultado se documenta. **Es un punto de control**: Task 6 no empieza hasta tener este resultado. Tasks 2 a 5 no dependen de él y pueden avanzar mientras tanto.

**Files:**
- Create: `ComandoDiagnosticoTraslaposVariables.cs`
- Modify: `App.cs` (botón dentro del bloque `#if DEBUG`, después del de `ComandoDiagnosticoFormasPropias`)
- Create: `docs/planes/2026-10-17-diagnostico-traslapos-variables.md`

**Interfaces:**
- Consumes: `DiagnosticoUI.MostrarTexto(string, string)` y `FiltroCualquierElemento` (en `ComandoDiagnosticoRebar.cs`), `EstandarCQ.Buscar/APies`.
- Produces: nada de código para otras tareas. Produce el **método de partido validado** que Task 6 copia: una llamada a `SpliceRebar` por traslapo, de abajo hacia arriba, cada una sobre la pieza de más arriba que devolvió la llamada anterior.

- [ ] **Step 1: Escribir el comando**

```csharp
namespace practicacq;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using practicacq.Modelo;

/// <summary>
/// TEMPORAL -- mide lo que pide la sección 10 de
/// docs/planes/2026-10-17-diseno-metodologia-traslapos.md antes de programar
/// la generación con traslapos de largo variable:
///  1. si una varilla se puede partir con traslapos de LARGOS DISTINTOS (un
///     tipo de empalme por largo, una llamada a SpliceRebar por traslapo,
///     cada una sobre la pieza de más arriba de la llamada anterior);
///  2. si cada traslapo queda centrado donde se pidió y con el largo pedido;
///  3. si el largo de corte que informa Revit coincide con recta + ganchos
///     del cuadro de la empresa;
///  4. si la reasignación de host sigue funcionando sobre esas piezas.
///
/// Las barras de prueba QUEDAN en el modelo para mirarlas en 3D; se sacan con
/// un solo Ctrl+Z. Sacar el comando (y su botón) al cerrar este cambio.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ComandoDiagnosticoTraslaposVariables : IExternalCommand
{
    private const string Titulo = "Diagnóstico traslapos de largo variable";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        IList<Reference> refs;
        try
        {
            refs = uidoc.Selection.PickObjects(
                ObjectType.Element, new FiltroCualquierElemento(),
                "Selecciona TODAS las piezas de una pila (zapata/dado y columnas) -- Finish (Enter) para terminar.");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }

        var sb = new StringBuilder();
        List<(Element Elemento, BoundingBoxXYZ Caja)> pila = refs
            .Select(r => doc.GetElement(r))
            .Where(e => e is not Rebar)
            .Select(e => (Elemento: e, Caja: e.get_BoundingBox(null)))
            .Where(p => p.Caja is not null)
            .Select(p => (p.Elemento, p.Caja!))
            .OrderBy(p => p.Item2.Min.Z)
            .ToList();
        List<(Element Elemento, BoundingBoxXYZ Caja)> columnas = pila
            .Where(p => p.Elemento.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralColumns)
            .ToList();
        if (columnas.Count == 0)
        {
            DiagnosticoUI.MostrarTexto("No se seleccionó ninguna columna estructural.", Titulo);
            return Result.Failed;
        }

        RebarBarType? barra = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
            .OrderBy(b => Math.Abs(EstandarCQ.DiametroMm(b) - 12.7)).FirstOrDefault();
        RebarHookType? gancho = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .Where(g => g.Style == RebarStyle.Standard)
            .OrderBy(g => Math.Abs(g.HookAngle - Math.PI / 2)).FirstOrDefault();
        CuadroVarillas.Varilla? varilla = barra is null ? null : EstandarCQ.Buscar(barra);
        if (barra is null || varilla is null)
        {
            DiagnosticoUI.MostrarTexto("No hay una barra #4 del cuadro en el modelo.", Titulo);
            return Result.Failed;
        }

        double xMin = columnas.Max(p => p.Caja.Min.X), xMax = columnas.Min(p => p.Caja.Max.X);
        double yMin = columnas.Max(p => p.Caja.Min.Y), yMax = columnas.Min(p => p.Caja.Max.Y);
        double zInicio = pila[0].Caja.Min.Z + Pies(0.075);
        double zFin = pila[^1].Caja.Max.Z;
        double z1 = zInicio + Pies(1.5), z2 = zInicio + Pies(4.5);

        sb.AppendLine($"Barra «{barra.Name}», gancho «{gancho?.Name ?? "(ninguno)"}». Varilla Z {M(zInicio):0.###} -> {M(zFin):0.###} m.");
        sb.AppendLine($"Traslapos pedidos: centro {M(z1):0.###} m y {M(z2):0.###} m.");

        var casos = new (string Nombre, (double Z, double Largo)[] Traslapos, bool UnaLlamada)[]
        {
            ("1: dos largos (0,75 y 0,95), una llamada por traslapo, abajo primero", new[] { (z1, 0.75), (z2, 0.95) }, false),
            ("2: dos largos (0,95 y 0,75), una llamada por traslapo, abajo primero", new[] { (z1, 0.95), (z2, 0.75) }, false),
            ("3: control -- mismo largo (0,75), los dos en UNA llamada (como hoy)", new[] { (z1, 0.75), (z2, 0.75) }, true),
            ("4: largo de corte -- sin traslapos", Array.Empty<(double, double)>(), false),
        };

        var registro = new RegistroDeFallas(sb);
        using (var t = new Transaction(doc, Titulo))
        {
            t.Start();
            FailureHandlingOptions opciones = t.GetFailureHandlingOptions();
            opciones.SetFailuresPreprocessor(registro);
            opciones.SetClearAfterRollback(true);
            t.SetFailureHandlingOptions(opciones);

            try
            {
                for (int i = 0; i < casos.Length; i++)
                {
                    (string nombre, (double Z, double Largo)[] traslapos, bool unaLlamada) = casos[i];
                    sb.AppendLine();
                    sb.AppendLine($"=== Caso {nombre} ===");

                    var punto = new XYZ(xMin + Pies(0.07) + i * Pies(0.04), yMin + Pies(0.07), 0);
                    Rebar? entera = Crear(doc, columnas[0].Elemento, barra, gancho, punto, zInicio, zFin);
                    if (entera is null) { sb.AppendLine("  CreateFromCurves devolvió null."); continue; }
                    doc.Regenerate();

                    double recta = M(zFin - zInicio);
                    double esperado = recta + (gancho is null ? 0 : 2 * varilla.Gancho90);
                    sb.AppendLine(
                        $"  Entera: TotalLength {M(entera.TotalLength):0.###} m, REBAR_ELEM_LENGTH " +
                        $"{M(entera.get_Parameter(BuiltInParameter.REBAR_ELEM_LENGTH)?.AsDouble() ?? 0):0.###} m; " +
                        $"recta {recta:0.###} + 2 ganchos del cuadro ({varilla.Gancho90:0.###}) = {esperado:0.###} m.");
                    if (traslapos.Length == 0) continue;

                    List<Rebar> piezas = unaLlamada
                        ? PartirEnUnaLlamada(doc, entera, barra, traslapos, sb)
                        : PartirUnaPorTraslapo(doc, entera, barra, traslapos, sb);
                    Medir(doc, piezas, traslapos, columnas, sb);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"EXCEPCIÓN no prevista: {ex.GetType().Name}: {ex.Message}");
            }

            sb.AppendLine();
            sb.AppendLine("=== AVISOS/ERRORES DE REVIT AL CONFIRMAR ===");
            TransactionStatus estado = t.Commit();
            if (registro.Cantidad == 0) sb.AppendLine("  (ninguno)");
            sb.AppendLine($"Estado final: {estado}. Las barras de prueba quedan en el modelo (Ctrl+Z las saca).");
        }

        DiagnosticoUI.MostrarTexto(sb.ToString(), Titulo);
        return Result.Succeeded;
    }

    private static Rebar? Crear(Document doc, Element host, RebarBarType barra, RebarHookType? gancho, XYZ planta, double z0, double z1)
    {
        using var terminaciones = new BarTerminationsData(doc);
        if (gancho is not null)
        {
            terminaciones.HookTypeIdAtStart = gancho.Id;
            terminaciones.HookTypeIdAtEnd = gancho.Id;
        }

        return Rebar.CreateFromCurves(
            doc, RebarStyle.Standard, barra, host, XYZ.BasisY,
            new List<Curve> { Line.CreateBound(new XYZ(planta.X, planta.Y, z0), new XYZ(planta.X, planta.Y, z1)) },
            terminaciones, useExistingShapeIfPossible: true, createNewShape: true);
    }

    /// <summary>Un tipo de empalme de prueba con exactamente ese solape.</summary>
    private static ElementId Tipo(Document doc, RebarBarType barra, double largo)
    {
        string nombre = $"CQ Diag Traslapo {largo.ToString("0.00", CultureInfo.InvariantCulture)} m";
        ElementId? id = RebarSpliceTypeUtils.GetAllRebarSpliceTypes(doc).FirstOrDefault(i => doc.GetElement(i)?.Name == nombre);
        id ??= RebarSpliceTypeUtils.CreateRebarSpliceType(doc, nombre).Id;
        RebarSpliceTypeUtils.SetLapLengthMultiplier(doc, id, Pies(largo) / barra.BarNominalDiameter);
        RebarSpliceTypeUtils.SetShiftOption(doc, id, RebarSpliceShiftOption.BarPlane);
        return id;
    }

    /// <summary>
    /// El método que Task 6 va a copiar: una llamada por traslapo, de abajo
    /// hacia arriba, cada una sobre la pieza de más arriba de la anterior.
    /// </summary>
    private static List<Rebar> PartirUnaPorTraslapo(
        Document doc, Rebar entera, RebarBarType barra, (double Z, double Largo)[] traslapos, StringBuilder sb)
    {
        Rebar? aPartir = entera;
        foreach ((double z, double largo) in traslapos.OrderBy(p => p.Z))
        {
            if (aPartir is null || !aPartir.IsValidObject) { sb.AppendLine("  No queda pieza para partir."); break; }
            (XYZ donde, XYZ _) = Extremos(aPartir);
            var opciones = new RebarSpliceOptions(doc, Tipo(doc, barra, largo), RebarSplicePosition.Middle);
            var geometria = new RebarSpliceGeometry(new XYZ(donde.X, donde.Y, z), XYZ.BasisZ);
            sb.AppendLine($"  Pieza {aPartir.Id.Value}, traslapo {largo:0.00} m en Z={M(z):0.###}: " +
                          $"CanRebarBeSpliced = {RebarSpliceUtils.CanRebarBeSpliced(aPartir, opciones, geometria)}");
            try
            {
                IList<ElementId> ids = RebarSpliceUtils.SpliceRebar(doc, aPartir.Id, opciones, new List<RebarSpliceGeometry> { geometria });
                doc.Regenerate();
                sb.AppendLine($"    SpliceRebar devolvió {ids.Count} id(s): {string.Join(", ", ids.Select(i => i.Value))}; " +
                              $"la pieza partida {(aPartir.IsValidObject ? "sigue existiendo" : "ya no existe")}.");
                aPartir = ids.Select(i => doc.GetElement(i) as Rebar).Where(r => r is not null).Select(r => r!)
                    .OrderByDescending(r => Extremos(r).Abajo.Z).FirstOrDefault();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    SpliceRebar tiró {ex.GetType().Name}: {ex.Message}");
                try { doc.Regenerate(); } catch { /* best-effort */ }
                break;
            }
        }

        return Cadena(doc, aPartir ?? entera);
    }

    private static List<Rebar> PartirEnUnaLlamada(
        Document doc, Rebar entera, RebarBarType barra, (double Z, double Largo)[] traslapos, StringBuilder sb)
    {
        (XYZ donde, XYZ _) = Extremos(entera);
        var opciones = new RebarSpliceOptions(doc, Tipo(doc, barra, traslapos[0].Largo), RebarSplicePosition.Middle);
        List<RebarSpliceGeometry> geometrias = traslapos
            .Select(p => new RebarSpliceGeometry(new XYZ(donde.X, donde.Y, p.Z), XYZ.BasisZ)).ToList();
        try
        {
            IList<ElementId> ids = RebarSpliceUtils.SpliceRebar(doc, entera.Id, opciones, geometrias);
            doc.Regenerate();
            sb.AppendLine($"  SpliceRebar (una llamada) devolvió {ids.Count} id(s).");
            return Cadena(doc, ids.Select(i => doc.GetElement(i) as Rebar).FirstOrDefault(r => r is not null) ?? entera);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  SpliceRebar tiró {ex.GetType().Name}: {ex.Message}");
            return new List<Rebar>();
        }
    }

    private static List<Rebar> Cadena(Document doc, Rebar semilla) =>
        !semilla.IsValidObject ? new List<Rebar>()
        : RebarSpliceUtils.GetSpliceChain(semilla).Select(i => doc.GetElement(i) as Rebar)
            .Where(r => r is not null).Select(r => r!).OrderBy(r => Extremos(r).Abajo.Z).ToList();

    private static void Medir(
        Document doc, List<Rebar> piezas, (double Z, double Largo)[] pedidos,
        List<(Element Elemento, BoundingBoxXYZ Caja)> columnas, StringBuilder sb)
    {
        sb.AppendLine($"  Cadena de empalme: {piezas.Count} pieza(s) (se esperaban {pedidos.Length + 1}).");
        foreach (Rebar p in piezas)
        {
            (XYZ a, XYZ b) = Extremos(p);
            sb.AppendLine($"    Rebar {p.Id.Value} host {p.GetHostId().Value}: Z {M(a.Z):0.###} -> {M(b.Z):0.###}, " +
                          $"XY ({M(a.X):0.###}, {M(a.Y):0.###}), TotalLength {M(p.TotalLength):0.###} m");
        }

        List<(double Z, double Largo)> ordenados = pedidos.OrderBy(p => p.Z).ToList();
        for (int i = 1; i < piezas.Count && i - 1 < ordenados.Count; i++)
        {
            (XYZ _, XYZ arribaDeLaDeAbajo) = Extremos(piezas[i - 1]);
            (XYZ abajoDeLaDeArriba, XYZ _) = Extremos(piezas[i]);
            double largo = M(arribaDeLaDeAbajo.Z - abajoDeLaDeArriba.Z);
            double centro = M((arribaDeLaDeAbajo.Z + abajoDeLaDeArriba.Z) / 2);
            sb.AppendLine($"    Traslapo {i}: medido {largo:0.###} m centrado en {centro:0.###} m; " +
                          $"pedido {ordenados[i - 1].Largo:0.###} m centrado en {M(ordenados[i - 1].Z):0.###} m.");
        }

        foreach (Rebar p in piezas)
        {
            (XYZ a, XYZ b) = Extremos(p);
            double medio = (a.Z + b.Z) / 2;
            var columna = columnas.FirstOrDefault(c => medio >= c.Caja.Min.Z && medio <= c.Caja.Max.Z);
            if (columna.Elemento is null || columna.Elemento.Id == p.GetHostId()) continue;
            try
            {
                p.SetHostId(doc, columna.Elemento.Id);
                doc.Regenerate();
                sb.AppendLine($"    Rebar {p.Id.Value}: host pasado a {columna.Elemento.Id.Value}; " +
                              $"cadena ahora {RebarSpliceUtils.GetSpliceChain(p).Count} pieza(s).");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    Rebar {p.Id.Value}: SetHostId tiró {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static (XYZ Abajo, XYZ Arriba) Extremos(Rebar r)
    {
        List<XYZ> puntos = r.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0)
            .SelectMany(c => new[] { c.GetEndPoint(0), c.GetEndPoint(1) }).OrderBy(p => p.Z).ToList();
        return (puntos[0], puntos[^1]);
    }

    private static double M(double pies) => UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Meters);
    private static double Pies(double metros) => UnitUtils.ConvertToInternalUnits(metros, UnitTypeId.Meters);

    private sealed class RegistroDeFallas(StringBuilder sb) : IFailuresPreprocessor
    {
        public int Cantidad { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            bool hayError = false;
            foreach (FailureMessageAccessor falla in failuresAccessor.GetFailureMessages())
            {
                Cantidad++;
                FailureSeverity severidad = falla.GetSeverity();
                sb.AppendLine($"  [{severidad}] {falla.GetDescriptionText()} " +
                              $"(elementos: {string.Join(", ", falla.GetFailingElementIds().Select(id => id.Value))})");
                if (severidad == FailureSeverity.Warning) failuresAccessor.DeleteWarning(falla);
                else hayError = true;
            }

            return hayError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
```

- [ ] **Step 2: Agregar el botón en `App.cs`**, dentro del `#if DEBUG`, justo después del bloque de `ComandoDiagnosticoFormasPropias`:

```csharp
        // TEMPORAL -- ver comentario de ComandoDiagnosticoTraslaposVariables.cs.
        // Sacar este botón (y el comando) al cerrar la nueva metodología de traslapos.
        panelHerramientas.AddItem(CrearBoton(
            "ComandoDiagnosticoTraslaposVariables",
            "Diagnóstico\ntraslapos variables",
            "practicacq.ComandoDiagnosticoTraslaposVariables",
            "TEMPORAL: prueba partir una varilla con traslapos de largos distintos y mide el largo de corte. Deja barras de prueba (Ctrl+Z las saca).",
            dllPath, folder, "iconoCQ"));
```

- [ ] **Step 3: Compilar las dos versiones**

Run: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027` y `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2026 -p:RevitInstallDir=Rev2026\`
Expected: `0 Errores` en las dos.

- [ ] **Step 4: Desplegar**

Run: `Get-Process -Name Revit -ErrorAction SilentlyContinue` → no tiene que devolver nada. Si devuelve algo, pedirle a Luciano que cierre Revit y volver a comprobar. Después: `powershell -ExecutionPolicy Bypass -File tools/deploy.ps1 -Versions 2027 -TargetFramework net10.0-windows`.

- [ ] **Step 5: Pedirle a Luciano que lo corra**

Mensaje: "Abre el modelo de prueba, en la pestaña CQEING aprieta «Diagnóstico traslapos variables», selecciona el dado 781598 y las columnas 782104 y 782735, aprieta Enter y pégame el texto que aparece." Esperar el texto.

- [ ] **Step 6: Documentar en `docs/planes/2026-10-17-diagnostico-traslapos-variables.md`**

Secciones: Contexto (por qué se mide), Qué se midió (los 4 casos), Resultado (texto pegado, recortado a lo relevante), Conclusión. La conclusión responde con sí/no y números:

1. ¿Los casos 1 y 2 dejan 3 piezas en una sola cadena?
2. ¿Cada traslapo mide lo pedido (±5 mm) y está centrado en la Z pedida (±5 mm)?
3. ¿`TotalLength` del caso 4 coincide con recta + 2 × `Gancho90` (±1 cm)? Si no, anotar la diferencia por gancho.
4. ¿`SetHostId` deja la cadena intacta?
5. ¿Avisos de Revit al confirmar?

**Decisión:**
- Si 1, 2 y 4 dan **sí**: Task 6 sigue tal cual está escrita.
- Si 3 da **no**: anotar el largo de gancho que usa Revit. Antes de Task 6 hay que volver con Luciano para decidir si el cálculo usa ese valor; si es así, se agrega a Task 2 un parámetro de largo de gancho (no está escrito en este plan).
- Si 1 o 2 dan **no**: **parar** y volver a hablar con Luciano antes de Task 6. El spec dice que la alternativa (varillas sueltas sin empalme nativo) cambia el diseño.

---

### Task 2: `PlanTraslapos` -- el cálculo, con sus pruebas

**Files:**
- Create: `Modelo/PlanTraslapos.cs`
- Create: `tools/PruebasTraslapos/PruebasTraslapos.csproj`
- Create: `tools/PruebasTraslapos/Program.cs`
- Modify: `practicacq.csproj` (excluir `tools/PruebasTraslapos/**`)

**Interfaces:**
- Consumes: `Tramo`, `TipoTramo`, `ColumnaContexto` (`Modelo/ColumnaContexto.cs`); `CuadroVarillas.PorDiametro`, `Varilla.Traslapo`, `Varilla.Gancho90`, `Varilla.Numero`.
- Produces (los usan Tasks 3, 4, 5 y 7):
  - `PlanTraslapos.Redondeos : IReadOnlyList<double>` = {0.10, 0.20, 0.25, 0.50, 1.00}; `PlanTraslapos.RedondeoPorDefecto = 0.50`.
  - `record PlanTraslapos.Columna(IReadOnlyList<Tramo> Tramos, double ZInicio, double ZFin, bool ConGanchos)` con `static Columna De(ColumnaContexto c)`.
  - `record PlanTraslapos.Opciones(double LargoComercial, double Redondeo, bool SolapeMayorDeLosDos)`.
  - `record PlanTraslapos.Pieza(double ZDesde, double ZHasta, double LargoCorte)`.
  - `record PlanTraslapos.Traslapo(double ZInferior, double ZSuperior)` con `Largo` y `Centro`.
  - `record PlanTraslapos.PlanTipo(string Nombre, IReadOnlyList<Pieza> Piezas, IReadOnlyList<Traslapo> Traslapos, double? SolapeMinimo, bool EnTramosDelOtro, string? Motivo)` con `bool TieneSolucion`.
  - `record PlanTraslapos.Resultado(PlanTipo A, PlanTipo B)` con `bool TieneSolucion`.
  - `static Resultado PlanTraslapos.Calcular(Columna columna, double diametroAMm, double diametroBMm, Opciones opciones)`.

- [ ] **Step 1: Excluir la carpeta de pruebas del add-in.** En `practicacq.csproj`, en `<DefaultItemExcludes>`, agregar `;tools/PruebasTraslapos/**` justo después de `tools/PreviewUI/**`. (Sin esto el add-in barre `Program.cs` y no compila.)

- [ ] **Step 2: Crear el proyecto de pruebas** `tools/PruebasTraslapos/PruebasTraslapos.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!--
    Pruebas del cálculo de traslapos (Modelo/PlanTraslapos.cs), sin Revit.
    Consola sin dependencias: corre con `dotnet run --project tools/PruebasTraslapos`
    y sale con código 1 si alguna falla. Los archivos se enlazan, no se copian
    (mismo criterio que tools/PreviewUI). practicacq.csproj excluye esta carpeta.
  -->
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="..\..\Modelo\PlanTraslapos.cs" Link="Modelo\PlanTraslapos.cs" />
    <Compile Include="..\..\Modelo\CuadroVarillas.cs" Link="Modelo\CuadroVarillas.cs" />
    <Compile Include="..\..\Modelo\ColumnaContexto.cs" Link="Modelo\ColumnaContexto.cs" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Escribir las pruebas** `tools/PruebasTraslapos/Program.cs`. Los valores esperados de los casos exactos se calcularon a mano con las reglas del spec (ver comentarios).

```csharp
using System.Diagnostics;
using practicacq.Modelo;
using static practicacq.Modelo.PlanTraslapos;

const double N4 = 12.7, N6 = 19.1;
int fallas = 0;

void Probar(string nombre, Action prueba)
{
    try { prueba(); Console.WriteLine($"OK     {nombre}"); }
    catch (Exception ex) { fallas++; Console.WriteLine($"FALLA  {nombre}: {ex.Message}"); }
}

void Igual(double esperado, double real, string que)
{
    if (Math.Abs(esperado - real) > 1e-6) throw new Exception($"{que}: esperado {esperado}, real {real}");
}

void Cierto(bool condicion, string que)
{
    if (!condicion) throw new Exception(que);
}

static Tramo T(double inf, double sup, TipoTramo tipo = TipoTramo.Libre) =>
    new() { ZInferior = inf, ZSuperior = sup, Tipo = tipo };

void Piezas(PlanTipo plan, params double[] largos)
{
    Cierto(plan.TieneSolucion, $"tipo {plan.Nombre} sin solución: {plan.Motivo}");
    Cierto(plan.Piezas.Count == largos.Length, $"tipo {plan.Nombre}: {plan.Piezas.Count} piezas, se esperaban {largos.Length}");
    for (int i = 0; i < largos.Length; i++) Igual(largos[i], plan.Piezas[i].LargoCorte, $"tipo {plan.Nombre}, pieza {i + 1}");
}

void UnTraslapo(PlanTipo plan, double inf, double sup)
{
    Cierto(plan.Traslapos.Count == 1, $"tipo {plan.Nombre}: {plan.Traslapos.Count} traslapos, se esperaba 1");
    Igual(inf, plan.Traslapos[0].ZInferior, $"tipo {plan.Nombre}, traslapo desde");
    Igual(sup, plan.Traslapos[0].ZSuperior, $"tipo {plan.Nombre}, traslapo hasta");
}

// Las reglas obligatorias del spec, sobre cualquier plan con solución.
void CumpleReglas(Columna col, Opciones op, Resultado r)
{
    List<Tramo> libres = col.Tramos.Where(t => t.Tipo != TipoTramo.DentroDeViga).OrderBy(t => t.ZInferior).ToList();
    foreach (PlanTipo p in new[] { r.A, r.B })
    {
        if (!p.TieneSolucion) continue;
        Cierto(p.Traslapos.Count == p.Piezas.Count - 1, $"{p.Nombre}: traslapos = piezas - 1");
        Igual(col.ZInicio, p.Piezas[0].ZDesde, $"{p.Nombre}: arranque");
        Igual(col.ZFin, p.Piezas[^1].ZHasta, $"{p.Nombre}: remate");
        foreach (Pieza pz in p.Piezas)
        {
            double pasos = pz.LargoCorte / op.Redondeo;
            Cierto(Math.Abs(pasos - Math.Round(pasos)) < 1e-6, $"{p.Nombre}: {pz.LargoCorte} no es múltiplo de {op.Redondeo}");
            Cierto(pz.LargoCorte <= op.LargoComercial + 1e-9, $"{p.Nombre}: {pz.LargoCorte} supera {op.LargoComercial}");
        }

        for (int i = 0; i < p.Traslapos.Count; i++)
        {
            Traslapo t = p.Traslapos[i];
            Cierto(t.Largo >= p.SolapeMinimo!.Value - 1e-9, $"{p.Nombre}: traslapo {t.Largo} menor que {p.SolapeMinimo}");
            Igual(p.Piezas[i].ZHasta, t.ZSuperior, $"{p.Nombre}: pieza {i + 1} termina donde termina su traslapo");
            Igual(p.Piezas[i + 1].ZDesde, t.ZInferior, $"{p.Nombre}: pieza {i + 2} arranca donde arranca su traslapo");
            Cierto(col.Tramos.Where(n => n.Tipo == TipoTramo.DentroDeViga)
                       .All(n => t.ZSuperior <= n.ZInferior + 1e-9 || t.ZInferior >= n.ZSuperior - 1e-9),
                   $"{p.Nombre}: traslapo {t.ZInferior}-{t.ZSuperior} toca un nudo");
            int ordinal = libres.FindIndex(l => t.ZInferior >= l.ZInferior - 1e-9 && t.ZSuperior <= l.ZSuperior + 1e-9) + 1;
            Cierto(ordinal > 0, $"{p.Nombre}: traslapo {t.ZInferior}-{t.ZSuperior} fuera de todo tramo libre");
            if (!p.EnTramosDelOtro)
                Cierto((ordinal % 2 == 0) == (p.Nombre == "A"), $"{p.Nombre}: traslapo en un tramo del otro tipo");
        }
    }

    if (r.A.TieneSolucion && r.B.TieneSolucion)
        foreach (Traslapo a in r.A.Traslapos)
            Cierto(r.B.Traslapos.All(b => a.ZSuperior <= b.ZInferior + 1e-9 || a.ZInferior >= b.ZSuperior - 1e-9),
                   $"traslapos de A y B se pisan en {a.ZInferior}-{a.ZSuperior}");
}

// C-12 del proveedor falso: zapata, 4 vigas de 0,45 m, 10 tramos.
var c12 = new Columna(new[]
{
    T(0.00, 0.20, TipoTramo.ContraZapata), T(0.20, 2.75), T(2.75, 3.20, TipoTramo.DentroDeViga),
    T(3.20, 5.95), T(5.95, 6.40, TipoTramo.DentroDeViga), T(6.40, 9.15), T(9.15, 9.60, TipoTramo.DentroDeViga),
    T(9.60, 12.35), T(12.35, 12.80, TipoTramo.DentroDeViga), T(12.80, 15.55),
}, -0.30, 15.55, ConGanchos: true);

Probar("una_varilla_exacta", () =>
{
    // 6,00 m justos, múltiplo de 0,50: una varilla, sin traslapos.
    var col = new Columna(new[] { T(0, 6.00) }, 0, 6.00, false);
    Resultado r = Calcular(col, N4, N4, new Opciones(12, 0.50, false));
    Piezas(r.A, 6.00);
    Piezas(r.B, 6.00);
    Cierto(r.A.EnTramosDelOtro, "A no tiene tramos propios en una columna de un tramo");
});

Probar("traslapo_forzado", () =>
{
    // 5,83 m entra en una barra de 12 pero no es múltiplo de 0,50: traslapo forzado.
    // B (tramo 1): el traslapo mínimo posible es 1,17 m (0,75 + lo que obliga el
    // redondeo); entre esos, el centrado es 2,33-3,50 con piezas de 3,50 y 3,50.
    // A (sin tramos propios) va en lo que dejó B: 0,83-2,00 empata en centrado
    // con 3,83-5,00 y gana el de más abajo.
    var col = new Columna(new[] { T(0, 5.83) }, 0, 5.83, false);
    Resultado r = Calcular(col, N4, N4, new Opciones(12, 0.50, false));
    Piezas(r.B, 3.50, 3.50);
    UnTraslapo(r.B, 2.33, 3.50);
    Piezas(r.A, 2.00, 5.00);
    UnTraslapo(r.A, 0.83, 2.00);
    CumpleReglas(col, new Opciones(12, 0.50, false), r);
});

Probar("ejemplo_de_luciano_esquiva_el_nudo", () =>
{
    // El ejemplo con el que se eligió el orden de preferencias (17-oct):
    // tramo 0-2,60, nudo 2,60-3,00, tramo 3,00-5,60; #4, redondeo 0,50, barra de 6.
    var col = new Columna(new[] { T(0, 2.60), T(2.60, 3.00, TipoTramo.DentroDeViga), T(3.00, 5.60) }, 0, 5.60, false);
    var op = new Opciones(6, 0.50, false);
    Resultado r = Calcular(col, N4, N4, op);
    Piezas(r.B, 1.50, 5.00);
    UnTraslapo(r.B, 0.60, 1.50);
    Piezas(r.A, 4.50, 2.00);
    UnTraslapo(r.A, 3.60, 4.50);
    CumpleReglas(col, op, r);
});

Probar("forzado_un_solo_tramo", () =>
{
    // Review Focus 1: sin zapata, el tramo arranca en la misma cota que la barra.
    // B: 1,10-2,00. A, en lo que queda: 0,10-1,00 (empata con 2,10-3,00, gana el de abajo).
    var col = new Columna(new[] { T(0, 3.10) }, 0, 3.10, false);
    var op = new Opciones(12, 0.50, false);
    Resultado r = Calcular(col, N4, N4, op);
    UnTraslapo(r.B, 1.10, 2.00);
    UnTraslapo(r.A, 0.10, 1.00);
    Cierto(r.A.EnTramosDelOtro && !r.B.EnTramosDelOtro, "solo A va en los tramos del otro");
    CumpleReglas(col, op, r);
});

Probar("sin_solucion_con_6_sugiere_12", () =>
{
    // Entrepisos de 4 m: con barra de 6 ni A ni B llegan de un tramo propio al siguiente.
    var col = new Columna(new[]
    {
        T(0, 3.60), T(3.60, 4.00, TipoTramo.DentroDeViga), T(4.00, 7.60),
        T(7.60, 8.00, TipoTramo.DentroDeViga), T(8.00, 11.60),
    }, 0, 11.60, false);
    Resultado con6 = Calcular(col, N4, N4, new Opciones(6, 0.50, false));
    Cierto(!con6.TieneSolucion, "con 6 m no debería haber solución");
    foreach (PlanTipo p in new[] { con6.A, con6.B })
        Cierto(p.Motivo is not null && p.Motivo.Contains("12 m"), $"{p.Nombre}: el motivo no sugiere 12 m: {p.Motivo}");
    Resultado con12 = Calcular(col, N4, N4, new Opciones(12, 0.50, false));
    Cierto(con12.TieneSolucion, $"con 12 m debería haber solución: {con12.A.Motivo} {con12.B.Motivo}");
    CumpleReglas(col, new Opciones(12, 0.50, false), con12);
});

Probar("barra_fuera_del_cuadro", () =>
{
    // Review Focus 3.
    Resultado r = Calcular(c12, 30.0, N4, new Opciones(12, 0.50, false));
    Cierto(!r.A.TieneSolucion && !string.IsNullOrWhiteSpace(r.A.Motivo), "A debería fallar con motivo");
    Cierto(r.B.TieneSolucion, $"B no depende de A: {r.B.Motivo}");
});

Probar("solape_mayor_de_los_dos", () =>
{
    Resultado cadaUno = Calcular(c12, N4, N6, new Opciones(12, 0.50, false));
    Igual(0.75, cadaUno.A.SolapeMinimo!.Value, "A con su propio solape");
    Igual(1.10, cadaUno.B.SolapeMinimo!.Value, "B con su propio solape");
    Resultado mayor = Calcular(c12, N4, N6, new Opciones(12, 0.50, true));
    Igual(1.10, mayor.A.SolapeMinimo!.Value, "A con el mayor");
    Igual(1.10, mayor.B.SolapeMinimo!.Value, "B con el mayor");
});

Probar("reglas_en_c12_con_todos_los_redondeos", () =>
{
    foreach (double redondeo in Redondeos)
    foreach (double comercial in new[] { 6.0, 12.0 })
    {
        var op = new Opciones(comercial, redondeo, false);
        CumpleReglas(c12, op, Calcular(c12, N4, N4, op));
    }

    Cierto(Calcular(c12, N4, N4, new Opciones(12, 0.50, false)).TieneSolucion, "C-12 con 12 m y 0,50 debería tener solución");
});

Probar("rendimiento_c12", () =>
{
    // Review Focus 4: la ventana recalcula en cada cambio de combo.
    var reloj = Stopwatch.StartNew();
    Calcular(c12, N4, N4, new Opciones(12, 0.10, false));
    reloj.Stop();
    Cierto(reloj.ElapsedMilliseconds < 1000, $"tardó {reloj.ElapsedMilliseconds} ms");
});

Console.WriteLine(fallas == 0 ? "0 fallas" : $"{fallas} falla(s)");
return fallas == 0 ? 0 : 1;
```

- [ ] **Step 4: Correr las pruebas y ver que fallan**

Run: `dotnet run --project tools/PruebasTraslapos`
Expected: error de compilación (`PlanTraslapos` no existe).

- [ ] **Step 5: Escribir `Modelo/PlanTraslapos.cs`**

```csharp
namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Dónde van los traslapos de la barra longitudinal de una columna y cuánto
/// mide cada varilla -- docs/planes/2026-10-17-diseno-metodologia-traslapos.md.
///
/// Sin Revit, en metros: lo usan la ventana (para dibujar A y B mientras se
/// configura, pestaña «Traslapos») y el generador (para crear exactamente
/// eso). Que sea el mismo cálculo es lo que garantiza que lo que se ve es lo
/// que se genera.
///
/// Reglas obligatorias (spec, sección 2): ningún traslapo toca un nudo; A
/// traslapa en los tramos libres pares y B en los impares (salvo que A no
/// tenga ninguno: entonces va en los de B, sin pisarlos); cada largo de corte
/// es múltiplo exacto del redondeo; ninguna varilla supera el largo
/// comercial; ningún traslapo baja del solape del cuadro. Entre los planes
/// que cumplen, gana el de menos traslapos, después el de menos exceso de
/// solape, después el más centrado y por último el de más abajo.
///
/// La búsqueda es exhaustiva (spec, sección 5): si existe un plan válido, lo
/// encuentra.
/// </summary>
public static class PlanTraslapos
{
    /// <summary>Pasos de redondeo que ofrece la ventana, en metros.</summary>
    public static readonly IReadOnlyList<double> Redondeos = new[] { 0.10, 0.20, 0.25, 0.50, 1.00 };

    public const double RedondeoPorDefecto = 0.50;

    /// <summary>La barra comercial más larga: la que se sugiere cuando con 6 m no hay solución.</summary>
    private const double LargoComercialMayor = 12.0;

    /// <summary>
    /// Lo que el cálculo necesita de la columna. <paramref name="ConGanchos"/>:
    /// la varilla lleva el gancho de 90° del cuadro en las dos puntas (hoy, solo
    /// cuando arranca sobre zapata).
    /// </summary>
    public sealed record Columna(IReadOnlyList<Tramo> Tramos, double ZInicio, double ZFin, bool ConGanchos)
    {
        /// <summary>
        /// Desde lo que lee la ventana. Hay zapata si el contexto la nombra o
        /// si la barra arranca más abajo que el estribo (el proveedor falso no
        /// nombra zapatas pero sí baja el arranque, ver C-12).
        /// </summary>
        public static Columna De(ColumnaContexto c) => new(
            c.Tramos, c.ZInferiorLongitudinal, c.ZSuperior,
            c.IdZapata is not null || c.ZInferiorLongitudinal < c.ZInferior - 1e-6);
    }

    public sealed record Opciones(double LargoComercial, double Redondeo, bool SolapeMayorDeLosDos);

    /// <summary>
    /// Una varilla. <see cref="ZDesde"/>/<see cref="ZHasta"/> son las cotas de
    /// su parte recta; <see cref="LargoCorte"/> incluye los ganchos de la
    /// primera y la última.
    /// </summary>
    public sealed record Pieza(double ZDesde, double ZHasta, double LargoCorte);

    public sealed record Traslapo(double ZInferior, double ZSuperior)
    {
        public double Largo => ZSuperior - ZInferior;
        public double Centro => (ZInferior + ZSuperior) / 2;
    }

    /// <param name="EnTramosDelOtro">El tipo no tenía tramos propios y traslapa en los del otro (spec, regla 2).</param>
    /// <param name="Motivo">Por qué no hay solución, en lenguaje llano; null si la hay.</param>
    public sealed record PlanTipo(
        string Nombre, IReadOnlyList<Pieza> Piezas, IReadOnlyList<Traslapo> Traslapos,
        double? SolapeMinimo, bool EnTramosDelOtro, string? Motivo)
    {
        public bool TieneSolucion => Motivo is null;
    }

    public sealed record Resultado(PlanTipo A, PlanTipo B)
    {
        public bool TieneSolucion => A.TieneSolucion && B.TieneSolucion;
    }

    public static Resultado Calcular(Columna columna, double diametroAMm, double diametroBMm, Opciones opciones)
    {
        Resultado resultado = CalcularCon(columna, diametroAMm, diametroBMm, opciones);
        if (resultado.TieneSolucion || opciones.LargoComercial >= LargoComercialMayor - 1e-9) return resultado;

        // Spec, sección 6: si con 6 m no hay forma, se dice si con 12 sí la hay.
        // No se cambia solo: la elección sigue siendo del usuario.
        Resultado con12 = CalcularCon(columna, diametroAMm, diametroBMm, opciones with { LargoComercial = LargoComercialMayor });
        if (!con12.TieneSolucion) return resultado;

        static PlanTipo Sugerir(PlanTipo p) =>
            p.TieneSolucion ? p : p with { Motivo = $"{p.Motivo} Con barras de 12 m sí se puede." };
        return new Resultado(Sugerir(resultado.A), Sugerir(resultado.B));
    }

    private static Resultado CalcularCon(Columna columna, double diametroAMm, double diametroBMm, Opciones opciones)
    {
        CuadroVarillas.Varilla? varillaA = CuadroVarillas.PorDiametro(diametroAMm);
        CuadroVarillas.Varilla? varillaB = CuadroVarillas.PorDiametro(diametroBMm);

        double? solapeA = varillaA?.Traslapo, solapeB = varillaB?.Traslapo;
        if (opciones.SolapeMayorDeLosDos && solapeA is double sa && solapeB is double sb)
            solapeA = solapeB = Math.Max(sa, sb);

        // Ordinal de los tramos libres de TODA la columna, de abajo hacia
        // arriba, desde 1: el mismo que usaba el generador antes de esto.
        List<Tramo> libres = columna.Tramos
            .Where(t => t.Tipo != TipoTramo.DentroDeViga)
            .OrderBy(t => t.ZInferior)
            .ToList();
        List<Rango> deA = libres.Where((_, i) => (i + 1) % 2 == 0).Select(Rango.De).ToList();
        List<Rango> deB = libres.Where((_, i) => (i + 1) % 2 == 1).Select(Rango.De).ToList();

        // El tramo libre 1 es siempre de B, así que solo A puede quedarse sin
        // tramos propios (columna de un piso). Entonces se calcula primero B y
        // A va en lo que B dejó libre (spec, regla 2).
        if (deA.Count == 0 && deB.Count > 0)
        {
            PlanTipo b = Resolver("B", columna, varillaB, diametroBMm, solapeB, opciones, deB, enTramosDelOtro: false);
            PlanTipo a = b.TieneSolucion
                ? Resolver("A", columna, varillaA, diametroAMm, solapeA, opciones, Restar(deB, b.Traslapos), enTramosDelOtro: true)
                : Fallido("A", solapeA, true, $"el tipo A no tiene tramos propios y va en los del B, que no tiene solución. {b.Motivo}");
            return new Resultado(a, b);
        }

        return new Resultado(
            Resolver("A", columna, varillaA, diametroAMm, solapeA, opciones, deA, enTramosDelOtro: false),
            Resolver("B", columna, varillaB, diametroBMm, solapeB, opciones, deB, enTramosDelOtro: false));
    }

    /// <summary>Un tramo (o un pedazo de tramo) donde puede ir un traslapo; <see cref="Centro"/> es el del tramo entero.</summary>
    private readonly record struct Rango(double Inf, double Sup, double Centro)
    {
        public static Rango De(Tramo t) => new(t.ZInferior, t.ZSuperior, (t.ZInferior + t.ZSuperior) / 2);
    }

    /// <summary>Los rangos menos lo que ocupan los traslapos del otro tipo.</summary>
    private static List<Rango> Restar(List<Rango> rangos, IReadOnlyList<Traslapo> ocupados)
    {
        var libres = new List<Rango>();
        foreach (Rango r in rangos)
        {
            double desde = r.Inf;
            foreach (Traslapo t in ocupados.Where(t => t.ZSuperior > r.Inf && t.ZInferior < r.Sup).OrderBy(t => t.ZInferior))
            {
                if (t.ZInferior > desde) libres.Add(r with { Inf = desde, Sup = t.ZInferior });
                desde = Math.Max(desde, t.ZSuperior);
            }

            if (r.Sup > desde) libres.Add(r with { Inf = desde });
        }

        return libres;
    }

    private static PlanTipo Resolver(
        string nombre, Columna columna, CuadroVarillas.Varilla? varilla, double diametroMm, double? solape,
        Opciones opciones, List<Rango> rangos, bool enTramosDelOtro)
    {
        if (varilla is null)
            return Fallido(nombre, null, enTramosDelOtro,
                $"la barra del tipo {nombre} (Ø {diametroMm:0.#} mm) no está en el cuadro de la empresa: " +
                "no se conoce su gancho ni su solape.");

        var calculo = new Busqueda(columna, columna.ConGanchos ? varilla.Gancho90 : 0, solape, opciones, rangos);
        return calculo.Resolver() is { } plan
            ? new PlanTipo(nombre, plan.Piezas, plan.Traslapos, solape, enTramosDelOtro, null)
            : Fallido(nombre, solape, enTramosDelOtro, calculo.Motivo(nombre, varilla, enTramosDelOtro));
    }

    private static PlanTipo Fallido(string nombre, double? solape, bool enTramosDelOtro, string motivo) =>
        new(nombre, Array.Empty<Pieza>(), Array.Empty<Traslapo>(), solape, enTramosDelOtro, motivo);

    /// <summary>
    /// La búsqueda de un tipo, en milímetros enteros medidos a lo largo de la
    /// varilla estirada ("u"): 0 es la punta del gancho de abajo y
    /// <see cref="_h"/> la del gancho de arriba.
    ///
    /// Programación dinámica sobre el punto donde arranca cada varilla. Un
    /// estado es "acá arranca una varilla, y el traslapo que la une a la de
    /// abajo ocupó la ventana tal". Desde un estado se prueba cada largo de
    /// varilla (múltiplo del redondeo hasta el comercial): o llega justo a la
    /// punta de arriba (fin), o termina dentro de una ventana más alta y ahí
    /// se prueba cada arranque posible de la varilla siguiente. Los arranques
    /// van en pasos de 1 cm, más los que dejan a la última varilla con un
    /// largo redondo (absorben los milímetros sueltos de la altura real).
    ///
    /// El costo se suma por traslapo y se compara en orden (cantidad, exceso,
    /// descentrado, altura), así que el mínimo es el plan preferido del spec.
    /// Como cada estado solo lleva a estados más altos, alcanza con recorrerlos
    /// de abajo hacia arriba una vez.
    /// </summary>
    private sealed class Busqueda
    {
        /// <summary>Los traslapos se ubican en pasos de 1 cm.</summary>
        private const int Rejilla = 10;

        private readonly double _zInicio, _zFin;
        private readonly Opciones _opciones;
        private readonly double? _solapeMetros;
        private readonly int _gancho, _h, _paso, _pasosMax;
        private readonly int? _solape;
        private readonly List<Ventana> _ventanas;
        private readonly Estado[] _rejilla;   // u múltiplo de Rejilla: índice u / Rejilla
        private readonly Estado[] _especial;  // u = _h - k * _paso fuera de la rejilla: índice k
        private int _alcanceMaximo;

        private readonly record struct Ventana(int Inf, int Sup, int Centro2);

        private struct Estado
        {
            public bool Alcanzado;
            public (long Traslapos, long Exceso, long Descentrado, long Altura) Costo;
            public int Ventana;   // la del traslapo que llega a este arranque; -1 en el arranque de abajo
            public int Previo;    // dónde arranca la varilla de abajo; -1 en el arranque de abajo
            public int Fin;       // dónde termina la varilla de abajo
        }

        public Busqueda(Columna columna, double ganchoMetros, double? solape, Opciones opciones, List<Rango> rangos)
        {
            _zInicio = columna.ZInicio;
            _zFin = columna.ZFin;
            _opciones = opciones;
            _solapeMetros = solape;
            _gancho = Mm(ganchoMetros);
            _h = Mm(_zFin - _zInicio) + 2 * _gancho;
            _paso = Math.Max(Rejilla, Mm(opciones.Redondeo));
            _pasosMax = Mm(opciones.LargoComercial) / _paso;
            _solape = solape is double s ? Mm(s) : null;
            _ventanas = rangos
                .Select(r => new Ventana(U(Math.Max(r.Inf, _zInicio)), U(Math.Min(r.Sup, _zFin)), 2 * U(r.Centro)))
                .Where(v => v.Sup > v.Inf)
                .OrderBy(v => v.Inf)
                .ToList();
            _rejilla = new Estado[_h / Rejilla + 1];
            _especial = new Estado[_pasosMax + 1];
        }

        private static int Mm(double metros) => (int)Math.Round(metros * 1000);
        private int U(double z) => Mm(z - _zInicio) + _gancho;
        private double Z(int u) => _zInicio + (u - _gancho) / 1000.0;

        private ref Estado Celda(int u) =>
            ref (u % Rejilla == 0 ? ref _rejilla[u / Rejilla] : ref _especial[(_h - u) / _paso]);

        /// <summary>Todos los arranques posibles, de abajo hacia arriba.</summary>
        private IEnumerable<int> Posiciones()
        {
            var especiales = new List<int>();
            for (int k = _pasosMax; k >= 1; k--)
            {
                int u = _h - k * _paso;
                if (u > 0 && u % Rejilla != 0) especiales.Add(u);
            }

            int e = 0;
            for (int g = 0; g < _h; g += Rejilla)
            {
                while (e < especiales.Count && especiales[e] < g) yield return especiales[e++];
                yield return g;
            }

            while (e < especiales.Count) yield return especiales[e++];
        }

        public (List<Pieza> Piezas, List<Traslapo> Traslapos)? Resolver()
        {
            if (_pasosMax <= 0) return null;

            _rejilla[0] = new Estado { Alcanzado = true, Ventana = -1, Previo = -1 };
            int mejorUltima = -1;
            (long, long, long, long) costoMejor = default;

            foreach (int a in Posiciones())
            {
                Estado actual = Celda(a);
                if (!actual.Alcanzado) continue;
                _alcanceMaximo = Math.Max(_alcanceMaximo, a);

                // Un arranque fuera de la rejilla solo existe para que la
                // última varilla tenga un largo redondo: desde ahí solo se termina.
                bool soloTerminar = a % Rejilla != 0;

                for (int k = 1; k <= _pasosMax; k++)
                {
                    int b = a + k * _paso;
                    if (b > _h) break;
                    if (b == _h)
                    {
                        if (mejorUltima < 0 || actual.Costo.CompareTo(costoMejor) < 0)
                        {
                            mejorUltima = a;
                            costoMejor = actual.Costo;
                        }

                        break;
                    }

                    if (soloTerminar || _solape is not int solape) continue;

                    int w = VentanaQueContiene(b, actual.Ventana);
                    if (w < 0) continue;
                    Ventana v = _ventanas[w];

                    // El arranque siguiente, por encima del actual (si no, las
                    // dos varillas nacerían en el mismo punto) y dejando al
                    // menos el solape mínimo hasta b.
                    int desde = Math.Max(v.Inf, a + 1);
                    int hasta = b - solape;
                    if (hasta < desde) continue;

                    for (int ap = (desde + Rejilla - 1) / Rejilla * Rejilla; ap <= hasta; ap += Rejilla)
                        Relajar(ap, actual, a, b, w, solape, v);

                    if (_h % Rejilla != 0)
                        for (int k2 = 1; k2 <= _pasosMax; k2++)
                        {
                            int ap = _h - k2 * _paso;
                            if (ap >= desde && ap <= hasta) Relajar(ap, actual, a, b, w, solape, v);
                        }
                }
            }

            return mejorUltima < 0 ? null : Reconstruir(mejorUltima);
        }

        private int VentanaQueContiene(int u, int despuesDe)
        {
            for (int w = despuesDe + 1; w < _ventanas.Count; w++)
                if (u >= _ventanas[w].Inf && u <= _ventanas[w].Sup) return w;
            return -1;
        }

        private void Relajar(int ap, Estado desde, int a, int b, int w, int solape, Ventana v)
        {
            (long, long, long, long) costo = (
                desde.Costo.Traslapos + 1,
                desde.Costo.Exceso + (b - ap - solape),
                desde.Costo.Descentrado + Math.Abs(ap + b - v.Centro2),
                desde.Costo.Altura + ap + b);

            ref Estado destino = ref Celda(ap);
            if (destino.Alcanzado && destino.Costo.CompareTo(costo) <= 0) return;
            destino = new Estado { Alcanzado = true, Costo = costo, Ventana = w, Previo = a, Fin = b };
        }

        private (List<Pieza>, List<Traslapo>) Reconstruir(int arranqueUltima)
        {
            var piezas = new List<Pieza>();
            var traslapos = new List<Traslapo>();
            int desde = arranqueUltima, hasta = _h;
            while (true)
            {
                piezas.Add(new Pieza(Math.Max(Z(desde), _zInicio), Math.Min(Z(hasta), _zFin), (hasta - desde) / 1000.0));
                Estado e = Celda(desde);
                if (e.Previo < 0) break;
                traslapos.Add(new Traslapo(Z(desde), Z(e.Fin)));
                hasta = e.Fin;
                desde = e.Previo;
            }

            piezas.Reverse();
            traslapos.Reverse();
            return (piezas, traslapos);
        }

        public string Motivo(string nombre, CuadroVarillas.Varilla varilla, bool enTramosDelOtro)
        {
            string largoCorte = $"{_h / 1000.0:0.00} m";
            string barras = $"varillas de hasta {_opciones.LargoComercial:0.#} m (en múltiplos de {_opciones.Redondeo:0.00} m)";

            if (_solapeMetros is null)
                return $"tipo {nombre}: el cuadro de la empresa no da traslapo en columnas para la #{varilla.Numero}, " +
                       $"y su largo de corte ({largoCorte}) no entra justo en una sola de las {barras}.";

            string donde = enTramosDelOtro ? "un hueco libre de los tramos del otro tipo"
                : $"un tramo libre {(nombre == "A" ? "par" : "impar")}";

            if (_ventanas.Count == 0)
                return $"tipo {nombre}: no hay ningún {donde.Replace("un ", "")} donde traslapar, y su largo de corte " +
                       $"({largoCorte}) no entra justo en una sola de las {barras}.";

            string desde = _alcanceMaximo == 0 ? "desde el arranque" : $"desde la cota {Z(_alcanceMaximo):0.00} m";
            return $"tipo {nombre}: {desde}, ninguna de las {barras} llega a {donde} con lugar para un traslapo " +
                   $"de {_solapeMetros:0.00} m sin tocar un nudo.";
        }
    }
}
```

- [ ] **Step 6: Correr las pruebas**

Run: `dotnet run --project tools/PruebasTraslapos`
Expected: 9 líneas `OK`, `0 fallas`, código de salida 0.

Si un caso exacto (`traslapo_forzado`, `ejemplo_de_luciano_esquiva_el_nudo`, `forzado_un_solo_tramo`) falla, **no cambiar el valor esperado para que pase**: los valores salen de las reglas del spec, recalcularlos a mano y buscar el error en el código. Si el error resulta estar en el valor esperado, explicarlo en el reporte de la tarea.

- [ ] **Step 7: Compilar el add-in y la vista previa** (la vista previa enlaza `Modelo/*.cs`, así que ya incluye el archivo nuevo)

Run: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027` y `dotnet build tools/PreviewUI`
Expected: `0 Errores` en los dos.

---

### Task 3: `ConfigLongitudinal` -- barras A/B, redondeo, solape, y configuraciones viejas

**Files:**
- Modify: `Modelo/ConfigLongitudinal.cs`
- Modify: `tools/PruebasTraslapos/PruebasTraslapos.csproj` (enlazar `ConfigLongitudinal.cs` y `CajaMenor.cs`)
- Modify: `tools/PruebasTraslapos/Program.cs` (prueba de migración)

**Interfaces:**
- Consumes: `PlanTraslapos.RedondeoPorDefecto` (Task 2).
- Produces (los usan Tasks 4, 5 y 7): `ConfigLongitudinal.TipoBarraANombre : string?`, `TipoBarraBNombre : string?`, `RedondeoVarillas : double` (por defecto 0,50), `SolapeMayorDeLosDos : bool`, `bool MigrarBarrasAB()`.

- [ ] **Step 1: Enlazar los archivos en el proyecto de pruebas.** En `tools/PruebasTraslapos/PruebasTraslapos.csproj`, dentro del mismo `<ItemGroup>`:

```xml
    <Compile Include="..\..\Modelo\ConfigLongitudinal.cs" Link="Modelo\ConfigLongitudinal.cs" />
    <Compile Include="..\..\Modelo\CajaMenor.cs" Link="Modelo\CajaMenor.cs" />
```

- [ ] **Step 2: Escribir la prueba que falla.** En `Program.cs`, antes de la línea `Console.WriteLine(fallas == 0 ...`:

```csharp
Probar("migracion_config_vieja", () =>
{
    // Review Focus 2: una configuración guardada antes de los tipos A/B.
    var vieja = new ConfigLongitudinal { TipoBarraEsquinasNombre = "#5", TipoBarraCaraXNombre = "#4", TipoBarraCaraYNombre = "#3" };
    Cierto(vieja.MigrarBarrasAB(), "debería migrar");
    Cierto(vieja.TipoBarraANombre == "#5" && vieja.TipoBarraBNombre == "#4", $"A={vieja.TipoBarraANombre} B={vieja.TipoBarraBNombre}");
    Igual(0.50, vieja.RedondeoVarillas, "redondeo por defecto");
    Cierto(!vieja.SolapeMayorDeLosDos, "solape de cada diámetro por defecto");
    Cierto(!vieja.MigrarBarrasAB(), "una segunda vez no hace nada");

    var soloY = new ConfigLongitudinal { TipoBarraEsquinasNombre = "#5", TipoBarraCaraYNombre = "#3" };
    soloY.MigrarBarrasAB();
    Cierto(soloY.TipoBarraBNombre == "#3", "sin cara X, B toma la cara Y");

    var nueva = new ConfigLongitudinal { TipoBarraANombre = "#4", TipoBarraBNombre = "#4", RedondeoVarillas = 0.20, SolapeMayorDeLosDos = true };
    Cierto(!nueva.MigrarBarrasAB(), "una nueva no se migra");
    ConfigLongitudinal copia = nueva.Copiar();
    Cierto(copia.TipoBarraANombre == "#4" && copia.TipoBarraBNombre == "#4" && copia.SolapeMayorDeLosDos, "Copiar lleva A, B y solape");
    Igual(0.20, copia.RedondeoVarillas, "Copiar lleva el redondeo");
});
```

- [ ] **Step 3: Correr y ver que falla**

Run: `dotnet run --project tools/PruebasTraslapos`
Expected: error de compilación (`TipoBarraANombre` no existe).

- [ ] **Step 4: Implementar en `Modelo/ConfigLongitudinal.cs`.**

Reemplazar el bloque de las tres barras (líneas 23-27, desde `/// <summary>Las cuatro esquinas...` hasta `public string? TipoBarraCaraYNombre { get; set; }`) por:

```csharp
    /// <summary>
    /// Barra de las posiciones de tipo A: las pares del anillo del perímetro,
    /// empezando por la esquina inferior izquierda. A y B se alternan "una sí,
    /// una no" y traslapan en tramos distintos (A en los pares, B en los
    /// impares) -- docs/planes/2026-10-17-diseno-metodologia-traslapos.md.
    /// Reemplaza a la elección por cara (esquinas / cara X / cara Y).
    /// </summary>
    public string? TipoBarraANombre { get; set; }

    /// <summary>Barra de las posiciones de tipo B: las impares del anillo. Ver <see cref="TipoBarraANombre"/>.</summary>
    public string? TipoBarraBNombre { get; set; }

    /// <summary>
    /// Todo largo de corte de varilla (ganchos incluidos) es múltiplo de esto,
    /// en metros, para que en obra sea fácil de cortar. Ver
    /// <see cref="PlanTraslapos.Redondeos"/>.
    /// </summary>
    public double RedondeoVarillas { get; set; } = PlanTraslapos.RedondeoPorDefecto;

    /// <summary>
    /// true: A y B traslapan con el mayor de sus dos solapes del cuadro;
    /// false: cada uno con el de su diámetro.
    /// </summary>
    public bool SolapeMayorDeLosDos { get; set; }

    /// <summary>OBSOLETO: solo para leer configuraciones guardadas antes de los tipos A/B. Ver <see cref="MigrarBarrasAB"/>.</summary>
    public string? TipoBarraEsquinasNombre { get; set; }

    /// <summary>OBSOLETO: ver <see cref="TipoBarraEsquinasNombre"/>.</summary>
    public string? TipoBarraCaraXNombre { get; set; }

    /// <summary>OBSOLETO: ver <see cref="TipoBarraEsquinasNombre"/>.</summary>
    public string? TipoBarraCaraYNombre { get; set; }
```

Reemplazar el comentario de `TipoEmpalmeNombre` (líneas 64-69) por:

```csharp
    /// <summary>
    /// OBSOLETO: el tipo de empalme ya no se elige. Cada traslapo puede tener
    /// un largo distinto y el add-in crea el tipo que corresponde
    /// (<c>Logica/TiposEmpalme</c>). Queda solo para leer configuraciones viejas.
    /// </summary>
```

Agregar, antes de `public ConfigLongitudinal Copiar()`:

```csharp
    /// <summary>
    /// Pasa una configuración guardada antes de los tipos A/B al formato
    /// nuevo: A = la barra de esquinas, B = la de cara X (o la de cara Y si no
    /// había de cara X). Devuelve true si cambió algo, para avisar.
    /// </summary>
    public bool MigrarBarrasAB()
    {
        if (TipoBarraANombre is not null || TipoBarraBNombre is not null) return false;
        if (TipoBarraEsquinasNombre is null && TipoBarraCaraXNombre is null && TipoBarraCaraYNombre is null) return false;

        TipoBarraANombre = TipoBarraEsquinasNombre ?? TipoBarraCaraXNombre ?? TipoBarraCaraYNombre;
        TipoBarraBNombre = TipoBarraCaraXNombre ?? TipoBarraCaraYNombre ?? TipoBarraEsquinasNombre;
        return true;
    }
```

En `Copiar()`, agregar después de `TipoBarraCaraYNombre = TipoBarraCaraYNombre,`:

```csharp
        TipoBarraANombre = TipoBarraANombre,
        TipoBarraBNombre = TipoBarraBNombre,
        RedondeoVarillas = RedondeoVarillas,
        SolapeMayorDeLosDos = SolapeMayorDeLosDos,
```

- [ ] **Step 5: Correr las pruebas**

Run: `dotnet run --project tools/PruebasTraslapos`
Expected: 10 `OK`, `0 fallas`.

- [ ] **Step 6: Compilar**

Run: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027` y `dotnet build tools/PreviewUI`
Expected: `0 Errores`. (El generador y la ventana todavía usan los campos viejos: compilan porque los campos siguen existiendo.)

---

### Task 4: Ventana -- combos de barra A/B, redondeo y solape; fuera el tipo de empalme

**Files:**
- Modify: `Vistas/VentanaConfiguradorEstribos.xaml:299-312`
- Modify: `Vistas/VentanaConfiguradorEstribos.xaml.cs` (constructor ~204-242, constantes ~930-937, `ComboConfiguraciones_SelectionChanged` ~438-450, sección "acero longitudinal (L4)" ~1050-1105)
- Modify: `Modelo/IProveedorTipos.cs`, `Modelo/ProveedorTiposFalso.cs`, `Logica/ProveedorTiposReal.cs` (quitar `ListarEmpalmes`)

**Interfaces:**
- Consumes: `ConfigLongitudinal.TipoBarraANombre/TipoBarraBNombre/RedondeoVarillas/SolapeMayorDeLosDos/MigrarBarrasAB()` (Task 3), `PlanTraslapos.Redondeos/RedondeoPorDefecto` (Task 2).
- Produces (los usa Task 5): controles `ComboBarraA`, `ComboBarraB`, `ComboRedondeo`, `ComboSolape` (y el `ComboLongitudBarra` de siempre); campo `private readonly IReadOnlyList<TipoBarra> _barras;`; método `LeerLongitudinalDelPanel()` que ya devuelve los campos nuevos.

- [ ] **Step 1: XAML.** En `VentanaConfiguradorEstribos.xaml`, reemplazar desde `<TextBlock Text="Tipo barra cara X:" ...` hasta `<ComboBox x:Name="ComboTipoEmpalme" Height="24" />` (líneas 299-312) por:

```xml
                    <!-- Tipos A y B: se alternan "una sí, una no" alrededor del
                         perímetro y traslapan en tramos distintos. Reemplazan a
                         la elección por cara (esquinas / X / Y). -->
                    <TextBlock Text="Tipo barra A:" Style="{StaticResource Etiqueta}" />
                    <ComboBox x:Name="ComboBarraA" Height="24" />

                    <TextBlock Text="Tipo barra B:" Style="{StaticResource Etiqueta}" />
                    <ComboBox x:Name="ComboBarraB" Height="24" />
                    <TextBlock Text="A y B se alternan alrededor del perímetro (una sí, una no), empezando por la esquina inferior izquierda con A."
                               TextWrapping="Wrap" Foreground="Gray" FontSize="11" Margin="0,2,0,0" />

                    <TextBlock Text="Longitud de barra comercial (m):" Style="{StaticResource Etiqueta}" />
                    <ComboBox x:Name="ComboLongitudBarra" Height="24" />

                    <TextBlock Text="Redondeo de varillas:" Style="{StaticResource Etiqueta}" />
                    <ComboBox x:Name="ComboRedondeo" Height="24" ItemStringFormat="0.00 m" />

                    <TextBlock Text="Solape mínimo de los traslapos:" Style="{StaticResource Etiqueta}" />
                    <ComboBox x:Name="ComboSolape" Height="24" />
                    <TextBlock Text="El dibujo de A y B, con el largo de cada varilla y cada traslapo, está en la pestaña «Traslapos» de la elevación."
                               TextWrapping="Wrap" Foreground="Gray" FontSize="11" Margin="0,2,0,0" />
```

- [ ] **Step 2: Constructor.** En `VentanaConfiguradorEstribos.xaml.cs`:

a) Agregar el campo junto a `_tipos` (cerca de la línea 30):

```csharp
    /// <summary>Las barras del documento con su diámetro: la pestaña «Traslapos» necesita el diámetro de A y B.</summary>
    private readonly IReadOnlyList<TipoBarra> _barras;
```

b) Reemplazar

```csharp
        ComboBarraCaraX.ItemsSource = nombresDeBarra;
        ComboBarraCaraY.ItemsSource = nombresDeBarra;
        ComboBarraEsquinas.ItemsSource = nombresDeBarra;
```

por

```csharp
        ComboBarraA.ItemsSource = nombresDeBarra;
        ComboBarraB.ItemsSource = nombresDeBarra;
        _barras = barras;
```

c) Reemplazar

```csharp
        ComboTipoEmpalme.ItemsSource = new[] { EmpalmePorDefecto }.Concat(_tipos.ListarEmpalmes()).ToList();
        ComboTipoEmpalme.SelectedItem = EmpalmePorDefecto;
```

por

```csharp
        ComboRedondeo.ItemsSource = PlanTraslapos.Redondeos;
        ComboRedondeo.SelectedItem = PlanTraslapos.RedondeoPorDefecto;

        ComboSolape.ItemsSource = new[] { TextoSolapeCadaDiametro, TextoSolapeMayor };
        ComboSolape.SelectedItem = TextoSolapeCadaDiametro;
```

- [ ] **Step 3: Constantes.** Reemplazar el bloque de `EmpalmePorDefecto` (el `/// <summary>` de "Primera opción del combo de tipo de empalme" y la constante) por:

```csharp
    /// <summary>Opciones del combo «Solape mínimo»: <see cref="ConfigLongitudinal.SolapeMayorDeLosDos"/> false / true.</summary>
    private const string TextoSolapeCadaDiametro = "El de cada diámetro";
    private const string TextoSolapeMayor = "El mayor de los dos tipos (A y B)";
```

Y en el comentario de `NumeroVarillaLongitudinal`, cambiar "caras y esquinas" por "tipos A y B".

- [ ] **Step 4: Leer y mostrar.** Reemplazar `LeerLongitudinalDelPanel`, la parte de combos de `MostrarLongitudinalEnPanel` y `ConfigLongitudinalPorDefecto`:

```csharp
    private ConfigLongitudinal LeerLongitudinalDelPanel() => new()
    {
        BarrasPorCaraX = ComboBarrasX.SelectedItem as int? ?? 2,
        BarrasPorCaraY = ComboBarrasY.SelectedItem as int? ?? 2,
        TipoBarraANombre = ComboBarraA.SelectedItem as string,
        TipoBarraBNombre = ComboBarraB.SelectedItem as string,
        LongitudComercialBarra = ComboLongitudBarra.SelectedItem as double? ?? 12.0,
        RedondeoVarillas = ComboRedondeo.SelectedItem as double? ?? PlanTraslapos.RedondeoPorDefecto,
        SolapeMayorDeLosDos = Equals(ComboSolape.SelectedItem, TextoSolapeMayor),
        TipoGanchoLongitudinalNombre = ComboGanchoLongitudinal.SelectedItem as string,
        // Las posiciones de grapas/cajas no se leen del panel: se editan aparte
        // en L4 y viven en _longitudinalPorColumna hasta que este método las
        // pisa -- por eso Guardar... las vuelve a copiar explícitamente.
    };

    private void MostrarLongitudinalEnPanel(ConfigLongitudinal cfg)
    {
        ComboBarrasX.SelectedItem = cfg.BarrasPorCaraX;
        ComboBarrasY.SelectedItem = cfg.BarrasPorCaraY;
        ComboBarraA.SelectedItem = cfg.TipoBarraANombre;
        ComboBarraB.SelectedItem = cfg.TipoBarraBNombre;
        ComboLongitudBarra.SelectedItem = cfg.LongitudComercialBarra;

        // Un redondeo que no está en la lista (configuración editada a mano)
        // se muestra como el por defecto, en vez de dejar el combo vacío.
        ComboRedondeo.SelectedItem = PlanTraslapos.Redondeos.Contains(cfg.RedondeoVarillas)
            ? cfg.RedondeoVarillas
            : PlanTraslapos.RedondeoPorDefecto;
        ComboSolape.SelectedItem = cfg.SolapeMayorDeLosDos ? TextoSolapeMayor : TextoSolapeCadaDiametro;
        ComboGanchoLongitudinal.SelectedItem = cfg.TipoGanchoLongitudinalNombre;

        TextoEstribosMenores.Text = ResumenEstribosMenores(cfg);
    }
```

y en `ConfigLongitudinalPorDefecto`, reemplazar las tres líneas `TipoBarraCaraXNombre = ...`, `TipoBarraCaraYNombre = ...`, `TipoBarraEsquinasNombre = ...` por:

```csharp
        TipoBarraANombre = BarraPorDefecto(tipos, NumeroVarillaLongitudinal),
        TipoBarraBNombre = BarraPorDefecto(tipos, NumeroVarillaLongitudinal),
```

- [ ] **Step 5: Configuraciones guardadas viejas.** En `ComboConfiguraciones_SelectionChanged`, reemplazar

```csharp
        ConfigLongitudinal longitudinal = guardada.Longitudinal.Copiar();
```

por

```csharp
        ConfigLongitudinal longitudinal = guardada.Longitudinal.Copiar();
        bool eraVieja = longitudinal.MigrarBarrasAB();
```

y al final del método, después de asignar `TextoEstado.Text`, agregar:

```csharp
        if (eraVieja)
            TextoEstado.Text += " Era una configuración anterior a los tipos A/B: A tomó la barra de esquinas y B la de cara; revísalas.";
```

- [ ] **Step 6: Quitar `ListarEmpalmes`.**
  - `Modelo/IProveedorTipos.cs`: borrar el método `ListarEmpalmes()` con su comentario, y en el comentario de la interfaz cambiar `<see cref="ConfigLongitudinal.TipoBarraEsquinasNombre"/>` por `<see cref="ConfigLongitudinal.TipoBarraANombre"/>`.
  - `Modelo/ProveedorTiposFalso.cs`: borrar `ListarEmpalmes()`.
  - `Logica/ProveedorTiposReal.cs`: borrar `ListarEmpalmes()` con su comentario; si queda algún `using` sin uso por eso, borrarlo.

- [ ] **Step 7: Comprobar que no queda nada viejo en la ventana**

Run: `rg -n "ComboBarraCara|ComboBarraEsquinas|ComboTipoEmpalme|EmpalmePorDefecto|ListarEmpalmes" Vistas Modelo Logica`
Expected: sin resultados.

- [ ] **Step 8: Compilar y abrir la vista previa**

Run: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027`, `dotnet build tools/PreviewUI`, después `dotnet run --project tools/PreviewUI`.
Expected: compila; en la ventana, el panel muestra "Tipo barra A", "Tipo barra B" (las dos con la #4), "Redondeo de varillas" en "0.50 m" (o "0,50 m", según el idioma de Windows) y "Solape mínimo" en "El de cada diámetro". No aparece "Tipo de empalme". Guardar una configuración, cambiar los combos, volver a elegir la guardada: los combos vuelven a lo guardado.

---

### Task 5: Pestaña «Traslapos» -- dibujo de A y B, recalculado en vivo

**Files:**
- Create: `Vistas/PanelTraslapos.xaml`
- Create: `Vistas/PanelTraslapos.xaml.cs`
- Modify: `Vistas/VentanaConfiguradorEstribos.xaml:132-165` (pestañas en la elevación)
- Modify: `Vistas/VentanaConfiguradorEstribos.xaml.cs` (recalcular y cablear eventos)
- Modify: `tools/PreviewUI/PreviewUI.csproj` (enlazar el control nuevo)

**Interfaces:**
- Consumes: `PlanTraslapos.Calcular/Columna.De/Opciones/Resultado/PlanTipo` (Task 2); `ComboBarraA/B`, `ComboRedondeo`, `ComboSolape`, `ComboLongitudBarra`, `_barras`, `LeerLongitudinalDelPanel()` (Task 4).
- Produces: métodos `PanelTraslapos.Mostrar(ColumnaContexto, PlanTraslapos.Resultado, string barraA, string barraB)` y `PanelTraslapos.MostrarMensaje(string)`; en la ventana, el control se llama `PanelDeTraslapos`.

- [ ] **Step 1: `Vistas/PanelTraslapos.xaml`**

```xml
<UserControl x:Class="practicacq.Vistas.PanelTraslapos"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Pestaña «Traslapos» de la elevación: tipo A y tipo B a escala, con el
         largo de cada varilla y cada traslapo. Las notas (motivos de un tipo
         sin solución, avisos) van abajo, fuera del scroll. -->
    <DockPanel>
        <StackPanel x:Name="Notas" DockPanel.Dock="Bottom" Margin="0,6,0,0" />
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <Canvas x:Name="Lienzo" Width="350" Height="460" Background="Transparent" />
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

- [ ] **Step 2: `Vistas/PanelTraslapos.xaml.cs`**

```csharp
namespace practicacq.Vistas;

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using practicacq.Modelo;

/// <summary>
/// Dibuja lo que calculó <see cref="PlanTraslapos"/> para la columna activa:
/// la columna a escala con sus nudos (y la zapata, si la hay), y al lado las
/// varillas de tipo A y de tipo B, cada una con su largo de corte, y cada
/// traslapo con su largo y sus cotas. Un tipo sin solución sale en rojo con
/// el motivo debajo -- docs/planes/2026-10-17-diseno-metodologia-traslapos.md,
/// sección 8.
///
/// Las varillas consecutivas se dibujan corridas unos píxeles a un lado y al
/// otro, para que el traslapo se vea como dos líneas paralelas.
/// </summary>
public partial class PanelTraslapos : UserControl
{
    private static readonly Brush ColorA = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));
    private static readonly Brush ColorB = new SolidColorBrush(Color.FromRgb(0x04, 0x78, 0x57));
    private static readonly Brush ColorNudo = new SolidColorBrush(Color.FromArgb(0x60, 0x3B, 0x82, 0xF6));
    private static readonly Brush ColorZapata = new SolidColorBrush(Color.FromArgb(0x60, 0xB4, 0x53, 0x09));
    private static readonly Brush ColorContorno = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
    private static readonly Brush ColorTexto = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly Brush ColorError = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));

    private const double MargenSuperior = 36;
    private const double MargenInferior = 20;
    private const double XA = 125;
    private const double XB = 255;

    public PanelTraslapos() => InitializeComponent();

    /// <summary>Sin dibujo: solo un texto (por ejemplo, "Elige la barra A y la barra B").</summary>
    public void MostrarMensaje(string mensaje)
    {
        Lienzo.Children.Clear();
        Notas.Children.Clear();
        AgregarNota(mensaje, ColorTexto);
    }

    public void Mostrar(ColumnaContexto columna, PlanTraslapos.Resultado plan, string barraA, string barraB)
    {
        Lienzo.Children.Clear();
        Notas.Children.Clear();

        double zMin = Math.Min(columna.ZInferiorLongitudinal, columna.ZInferior);
        double zMax = columna.ZSuperior;
        double escala = Math.Max(1, Lienzo.Height - MargenSuperior - MargenInferior) / Math.Max(0.01, zMax - zMin);
        double Y(double z) => MargenSuperior + (zMax - z) * escala;

        // Fondo: zapata y nudos de lado a lado, después el contorno de la columna.
        if (columna.ZInferiorLongitudinal < columna.ZInferior - 1e-6)
            Rectangulo(8, Y(columna.ZInferior), 334, Y(zMin) - Y(columna.ZInferior) + 4, ColorZapata, null);
        foreach (Tramo nudo in columna.Tramos.Where(t => t.Tipo == TipoTramo.DentroDeViga))
            Rectangulo(8, Y(nudo.ZSuperior), 334, Y(nudo.ZInferior) - Y(nudo.ZSuperior), ColorNudo, null);
        Rectangulo(20, Y(zMax), 26, Y(columna.ZInferior) - Y(zMax), null, ColorContorno);

        DibujarTipo(plan.A, $"Tipo A · {barraA}", XA, ColorA, Y);
        DibujarTipo(plan.B, $"Tipo B · {barraB}", XB, ColorB, Y);

        foreach (PlanTraslapos.PlanTipo tipo in new[] { plan.A, plan.B })
        {
            if (!tipo.TieneSolucion)
                AgregarNota($"Tipo {tipo.Nombre} sin solución: {tipo.Motivo} Esta columna no va a generar su acero longitudinal.", ColorError);
            else if (tipo.EnTramosDelOtro)
                AgregarNota($"El tipo {tipo.Nombre} no tiene tramos propios: traslapa en los del otro tipo, a otra altura.", ColorTexto);
        }

        if (plan.A.SolapeMinimo is double sa && plan.B.SolapeMinimo is double sb)
            AgregarNota($"Solape mínimo: A {sa:0.00} m · B {sb:0.00} m. Los traslapos pueden ser más largos por el redondeo.", ColorTexto);
    }

    private void DibujarTipo(PlanTraslapos.PlanTipo tipo, string titulo, double x, Brush color, Func<double, double> Y)
    {
        Texto(titulo, x - 45, 4, tipo.TieneSolucion ? color : ColorError, negrita: true);
        if (!tipo.TieneSolucion)
        {
            Texto("sin solución", x - 30, 20, ColorError);
            return;
        }

        for (int i = 0; i < tipo.Piezas.Count; i++)
        {
            PlanTraslapos.Pieza pieza = tipo.Piezas[i];
            double xi = x + (i % 2 == 0 ? -3 : 3);
            Linea(xi, Y(pieza.ZDesde), xi, Y(pieza.ZHasta), color, 3);
            Texto($"{pieza.LargoCorte:0.00} m", x + 9, (Y(pieza.ZDesde) + Y(pieza.ZHasta)) / 2 - 8, color);
        }

        foreach (PlanTraslapos.Traslapo t in tipo.Traslapos)
        {
            Linea(x - 10, Y(t.ZSuperior), x - 10, Y(t.ZInferior), ColorTexto, 1);
            Texto($"{t.Largo:0.00} m", x - 62, Y(t.ZSuperior) - 2, ColorTexto);
            Texto($"{t.ZInferior:0.00}–{t.ZSuperior:0.00}", x - 62, Y(t.ZSuperior) + 10, ColorTexto, chico: true);
        }
    }

    private void Rectangulo(double x, double y, double ancho, double alto, Brush? relleno, Brush? borde)
    {
        var r = new Rectangle { Width = Math.Max(1, ancho), Height = Math.Max(1, alto), Fill = relleno, Stroke = borde };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        Lienzo.Children.Add(r);
    }

    private void Linea(double x1, double y1, double x2, double y2, Brush color, double grosor) =>
        Lienzo.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = color, StrokeThickness = grosor });

    private void Texto(string texto, double x, double y, Brush color, bool negrita = false, bool chico = false)
    {
        var t = new TextBlock
        {
            Text = texto, Foreground = color, FontSize = chico ? 9 : 10,
            FontWeight = negrita ? FontWeights.SemiBold : FontWeights.Normal,
        };
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
        Lienzo.Children.Add(t);
    }

    private void AgregarNota(string texto, Brush color) =>
        Notas.Children.Add(new TextBlock { Text = texto, Foreground = color, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
}
```

- [ ] **Step 3: Pestañas en la elevación.** En `VentanaConfiguradorEstribos.xaml`, reemplazar desde el comentario `<!-- Leyenda. WrapPanel y no StackPanel...` hasta el cierre `</ScrollViewer>` del `CanvasElevacion` (líneas 132-165) por lo mismo envuelto en pestañas (el contenido de la pestaña «Tramos» es **exactamente** el que había, sin cambios):

```xml
                    <TabControl x:Name="PestanasElevacion" BorderThickness="0" Background="Transparent">
                        <TabItem Header="Tramos">
                            <DockPanel>
                                <!-- (pegar acá, sin cambios, el WrapPanel de la leyenda,
                                     el Grid DockPanel.Dock="Bottom" de las flechas y el
                                     ScrollViewer con CanvasElevacion) -->
                            </DockPanel>
                        </TabItem>
                        <!-- Tipo A y tipo B con todas sus medidas, recalculado con
                             cada cambio del acero longitudinal (pedido de Luciano, 17-oct). -->
                        <TabItem Header="Traslapos">
                            <local:PanelTraslapos x:Name="PanelDeTraslapos" />
                        </TabItem>
                    </TabControl>
```

(El comentario "pegar acá" no queda en el archivo: se reemplaza por los tres elementos movidos.)

- [ ] **Step 4: Recalcular desde la ventana.** En `VentanaConfiguradorEstribos.xaml.cs`:

a) Campo nuevo junto a los otros flags de "corta el eco" (cerca de `_cargandoRecubrimiento`):

```csharp
    /// <summary>Corta los recálculos de la pestaña «Traslapos» mientras <see cref="MostrarLongitudinalEnPanel"/> llena los combos uno por uno.</summary>
    private bool _mostrandoLongitudinal;
```

b) Al final del constructor (después del bloque que cablea eventos por código):

```csharp
        // Cualquier cambio que mueva los traslapos recalcula la pestaña
        // «Traslapos». Cableado acá y no en el XAML por lo mismo que los de
        // arriba. El recubrimiento no participa: no cambia las alturas.
        foreach (ComboBox combo in new[] { ComboBarraA, ComboBarraB, ComboLongitudBarra, ComboRedondeo, ComboSolape })
            combo.SelectionChanged += (_, _) => { if (!_mostrandoLongitudinal) RecalcularTraslapos(); };
```

c) En `MostrarLongitudinalEnPanel`, envolver el cuerpo:

```csharp
    private void MostrarLongitudinalEnPanel(ConfigLongitudinal cfg)
    {
        _mostrandoLongitudinal = true;
        try
        {
            // ... (el cuerpo de Task 4, sin cambios)
        }
        finally
        {
            _mostrandoLongitudinal = false;
        }

        RecalcularTraslapos();
    }
```

d) Método nuevo, en la sección "acero longitudinal (L4)":

```csharp
    /// <summary>
    /// Vuelve a calcular y dibujar los traslapos de la columna activa con lo
    /// que hay ahora en el panel. Es el mismo cálculo que usa el generador
    /// (<see cref="PlanTraslapos"/>), así que lo que se ve es lo que se genera.
    /// </summary>
    private void RecalcularTraslapos()
    {
        if (_columnaActiva is null)
        {
            PanelDeTraslapos.MostrarMensaje("Elige una columna para ver sus traslapos.");
            return;
        }

        ConfigLongitudinal cfg = LeerLongitudinalDelPanel();
        double? diametroA = _barras.FirstOrDefault(b => b.Nombre == cfg.TipoBarraANombre)?.DiametroMm;
        double? diametroB = _barras.FirstOrDefault(b => b.Nombre == cfg.TipoBarraBNombre)?.DiametroMm;
        if (diametroA is null || diametroB is null)
        {
            PanelDeTraslapos.MostrarMensaje("Elige la barra A y la barra B para ver los traslapos.");
            return;
        }

        PlanTraslapos.Resultado plan = PlanTraslapos.Calcular(
            PlanTraslapos.Columna.De(_columnaActiva), diametroA.Value, diametroB.Value,
            new PlanTraslapos.Opciones(cfg.LongitudComercialBarra, cfg.RedondeoVarillas, cfg.SolapeMayorDeLosDos));
        PanelDeTraslapos.Mostrar(_columnaActiva, plan, cfg.TipoBarraANombre!, cfg.TipoBarraBNombre!);
    }
```

- [ ] **Step 5: Enlazar el control en la vista previa.** En `tools/PreviewUI/PreviewUI.csproj`, dentro del `<ItemGroup>` de las vistas, agregar:

```xml
    <Page Include="..\..\Vistas\PanelTraslapos.xaml">
      <Link>Vistas\PanelTraslapos.xaml</Link>
      <SubType>Designer</SubType>
      <Generator>MSBuild:Compile</Generator>
    </Page>
    <Compile Include="..\..\Vistas\PanelTraslapos.xaml.cs">
      <Link>Vistas\PanelTraslapos.xaml.cs</Link>
      <DependentUpon>PanelTraslapos.xaml</DependentUpon>
    </Compile>
```

- [ ] **Step 6: Compilar**

Run: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027` y `dotnet build tools/PreviewUI`
Expected: `0 Errores`.

- [ ] **Step 7: Mirarlo en la vista previa**

Run: `dotnet run --project tools/PreviewUI`
Comprobar, y anotar el resultado en el reporte de la tarea:
1. Elegir **C-12**, pestaña «Traslapos»: se ven la zapata, los 4 nudos en azul, A y B con piezas rotuladas; ningún traslapo cruza una banda azul.
2. Cambiar el redondeo a 0,10: el dibujo cambia enseguida y la ventana no se traba.
3. Elegir **C-1** (un solo tramo de 3,00 m, múltiplo de 0,50): A y B en una sola varilla de 3,00 m, nota "El tipo A no tiene tramos propios".
4. Cambiar la barra A a una que no esté en el cuadro (si el proveedor falso tiene alguna): A en rojo con el motivo, sin excepción.
5. Volver a la pestaña «Tramos»: la elevación de siempre, con las flechas y los ✓ funcionando.

---

### Task 6: `TiposEmpalme` y `BarraContinua` -- un tipo por largo, una llamada por traslapo

**Requisito:** Task 1 terminada con la decisión "Task 6 sigue tal cual". Si no, no empezar.

**Files:**
- Modify: `Logica/TiposEmpalme.cs`
- Modify: `Logica/BarraContinua.cs` (`Pedido`, `Crear`, `Partir`)
- Modify: `ComandoDiagnosticoBarraContinua.cs:365` (usa la firma vieja de `Resolver`)

**Interfaces:**
- Consumes: el método de partido validado en Task 1.
- Produces (los usa Task 7):
  - `TiposEmpalme.Resolver(RebarBarType barra, double largoMetros) : TiposEmpalme.Resultado` (debe llamarse dentro de una transacción).
  - `record struct BarraContinua.TraslapoPedido(double ZCentro, ElementId Tipo)` (Z en pies).
  - `BarraContinua.Pedido(RebarBarType Barra, double X, double Y, XYZ Normal, double ZInicio, double ZFin, IReadOnlyList<TraslapoPedido> Traslapos, IReadOnlyList<ColumnaDePila> Pila, RebarHookType? GanchoAbajo, RebarHookType? GanchoArriba, RebarTerminationOrientation OrientacionAbajo, RebarTerminationOrientation OrientacionArriba, Limites Limites)`.

- [ ] **Step 1: `TiposEmpalme` por largo.** Reemplazar la clase completa por:

```csharp
namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// El tipo de empalme (<c>RebarSpliceType</c>) de un traslapo de barra
/// longitudinal: uno PROPIO del add-in por varilla del cuadro y por largo
/// ("CQ Traslapo #4 · 0,90 m").
///
/// Hace falta uno por largo porque Revit fija el solape en el tipo (como
/// múltiplo del diámetro) y desde la nueva metodología cada traslapo puede
/// medir distinto: el del cuadro es solo el mínimo, y el redondeo de las
/// varillas lo estira (docs/planes/2026-10-17-diseno-metodologia-traslapos.md).
///
/// Una instancia por corrida: cachea para no crear ni reconfigurar el mismo
/// tipo una vez por varilla.
/// </summary>
public sealed class TiposEmpalme
{
    public const string PrefijoPropio = "CQ Traslapo #";

    public readonly record struct Resultado(ElementId? Tipo, string? Aviso);

    private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-CO");

    private readonly Document _doc;
    private readonly Dictionary<string, ElementId> _porNombre = new();
    private readonly Dictionary<(ElementId Barra, int Milimetros), Resultado> _cache = new();

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
    public Resultado Resolver(RebarBarType barra, double largoMetros)
    {
        var clave = (barra.Id, (int)Math.Round(largoMetros * 1000));
        if (_cache.TryGetValue(clave, out Resultado cacheado)) return cacheado;

        Resultado resultado;
        CuadroVarillas.Varilla? varilla = EstandarCQ.Buscar(barra);
        if (varilla is null)
        {
            resultado = new Resultado(null,
                $"la barra «{barra.Name}» no está en el cuadro de la empresa; sus varillas quedan enteras, sin traslapos.");
        }
        else
        {
            string nombre = $"{PrefijoPropio}{varilla.Numero} · {largoMetros.ToString("0.00", Espanol)} m";
            try
            {
                if (!_porNombre.TryGetValue(nombre, out ElementId? id))
                {
                    id = RebarSpliceTypeUtils.CreateRebarSpliceType(_doc, nombre).Id;
                    _porNombre[nombre] = id;
                }

                // Se vuelven a fijar en cada corrida, por si alguien los cambió
                // a mano: el nombre promete ese largo.
                RebarSpliceTypeUtils.SetLapLengthMultiplier(_doc, id, EstandarCQ.APies(largoMetros) / barra.BarNominalDiameter);
                RebarSpliceTypeUtils.SetShiftOption(_doc, id, RebarSpliceShiftOption.BarPlane);
                resultado = new Resultado(id, null);
            }
            catch (Exception ex)
            {
                resultado = new Resultado(null,
                    $"no se pudo preparar el tipo de empalme «{nombre}» ({GeometriaSeccion.Explicar(ex)}); " +
                    "ese traslapo no se aplica y la varilla queda más larga en ese punto.");
            }
        }

        _cache[clave] = resultado;
        return resultado;
    }
}
```

- [ ] **Step 2: Arreglar el diagnóstico viejo que usa la firma anterior.** En `ComandoDiagnosticoBarraContinua.cs:365`, reemplazar

```csharp
        TiposEmpalme.Resultado tipo = new TiposEmpalme(doc).Resolver(barra, null);
```

por

```csharp
        TiposEmpalme.Resultado tipo = new TiposEmpalme(doc).Resolver(barra, EstandarCQ.Buscar(barra)?.Traslapo ?? 0.75);
```

- [ ] **Step 3: `BarraContinua.Pedido`.** Reemplazar el record `Pedido` (líneas 37-42) por:

```csharp
    /// <summary>Un traslapo a aplicar: su altura central (pies) y el tipo de empalme con su largo.</summary>
    public readonly record struct TraslapoPedido(double ZCentro, ElementId Tipo);

    public sealed record Pedido(
        RebarBarType Barra, double X, double Y, XYZ Normal, double ZInicio, double ZFin,
        IReadOnlyList<TraslapoPedido> Traslapos, IReadOnlyList<ColumnaDePila> Pila,
        RebarHookType? GanchoAbajo, RebarHookType? GanchoArriba,
        RebarTerminationOrientation OrientacionAbajo, RebarTerminationOrientation OrientacionArriba,
        Limites Limites);
```

- [ ] **Step 4: `BarraContinua.Crear`.**
  - En la traza inicial, reemplazar `traslapos pedidos en Z: [{string.Join("; ", p.ZTraslapos.Select(Z))}]; tipo de empalme: {(p.TipoEmpalme is null ? "ninguno" : doc.GetElement(p.TipoEmpalme)?.Name)}` por `traslapos pedidos: [{string.Join("; ", p.Traslapos.Select(t => $"Z={Z(t.ZCentro)} «{doc.GetElement(t.Tipo)?.Name}»"))}]`.
  - Reemplazar `if (p.ZTraslapos.Count == 0 || p.TipoEmpalme is null)` por `if (p.Traslapos.Count == 0)`.
  - Reemplazar `int esperadas = p.ZTraslapos.Count - rechazados.Count + 1;` por `int esperadas = p.Traslapos.Count - rechazados.Count + 1;`.

- [ ] **Step 5: `BarraContinua.Partir`.** Reemplazar desde `var opciones = new RebarSpliceOptions(doc, p.TipoEmpalme!, RebarSplicePosition.Middle);` hasta el final de la función local `Intentar` (la llave que cierra `List<Rebar> Intentar(string etiqueta)`) por:

```csharp
        // Cada traslapo con su propio tipo de empalme: el largo puede ser
        // distinto en cada uno (el del cuadro es solo el mínimo).
        var aceptados = new List<TraslapoPedido>();
        foreach (TraslapoPedido t in p.Traslapos.OrderBy(t => t.ZCentro))
        {
            var opciones = new RebarSpliceOptions(doc, t.Tipo, RebarSplicePosition.Middle);
            RebarSpliceError puede = RebarSpliceUtils.CanRebarBeSpliced(
                entera, opciones, new RebarSpliceGeometry(new XYZ(abajo.X, abajo.Y, t.ZCentro), XYZ.BasisZ));
            Traza?.Invoke($"  CanRebarBeSpliced en Z={Z(t.ZCentro)}: {puede}");

            if (puede == RebarSpliceError.Success)
                aceptados.Add(t);
            else
                rechazados.Add($"Z={Z(t.ZCentro)} m ({puede})");
        }

        if (aceptados.Count == 0) return (new List<Rebar>(), entera);
        double primeraAltura = aceptados[0].ZCentro;

        // Un intento de partir, con la barra donde esté en ese momento. Una
        // llamada a SpliceRebar por traslapo, de abajo hacia arriba, cada una
        // sobre la pieza de más arriba que dejó la anterior -- el método medido
        // contra Revit real en docs/planes/2026-10-17-diagnostico-traslapos-variables.md.
        // Las piezas se leen de la cadena de empalme al final: lo que cuenta
        // es lo que quedó en el modelo.
        List<Rebar> Intentar(string etiqueta)
        {
            Rebar? aPartir = entera;
            foreach (TraslapoPedido t in aceptados)
            {
                if (aPartir is null || !aPartir.IsValidObject) break;

                (XYZ donde, XYZ _) = Extremos(aPartir);
                var opciones = new RebarSpliceOptions(doc, t.Tipo, RebarSplicePosition.Middle);
                IList<ElementId> resultantes;
                try
                {
                    resultantes = RebarSpliceUtils.SpliceRebar(
                        doc, aPartir.Id, opciones,
                        new List<RebarSpliceGeometry> { new(new XYZ(donde.X, donde.Y, t.ZCentro), XYZ.BasisZ) });
                }
                catch (Exception ex)
                {
                    Traza?.Invoke($"  [{etiqueta}] SpliceRebar en Z={Z(t.ZCentro)} tiró {ex.GetType().Name}: {ex.Message}");
                    try { doc.Regenerate(); } catch { /* best-effort */ }
                    break;
                }

                doc.Regenerate();
                Traza?.Invoke($"  [{etiqueta}] SpliceRebar en Z={Z(t.ZCentro)} devolvió {resultantes.Count} id(s)");
                aPartir = resultantes
                    .Select(id => doc.GetElement(id) as Rebar)
                    .Where(r => r is not null)
                    .Select(r => r!)
                    .OrderByDescending(r => Extremos(r).Abajo.Z)
                    .FirstOrDefault();
            }

            Rebar? semilla = aPartir is not null && aPartir.IsValidObject ? aPartir
                : entera.IsValidObject ? entera : null;
            List<Rebar> cadena = semilla is null
                ? new List<Rebar>()
                : RebarSpliceUtils.GetSpliceChain(semilla)
                    .Select(id => doc.GetElement(id) as Rebar)
                    .Where(r => r is not null)
                    .Select(r => r!)
                    .OrderBy(r => Extremos(r).Abajo.Z)
                    .ToList();

            Traza?.Invoke($"  [{etiqueta}] cadena de {cadena.Count} pieza(s)");
            return cadena.Count > 1 ? cadena : new List<Rebar>();
        }
```

Después, en el bucle que cambia de columna antes de partir, reemplazar las dos apariciones de `alturas[0]` por `primeraAltura`.

- [ ] **Step 6: Actualizar el comentario de cabecera de `BarraContinua`.** En la lista de lo medido, agregar al final:

```csharp
///  - (17-oct) con traslapos de largos distintos hace falta un tipo de
///    empalme por largo y una llamada a SpliceRebar por traslapo, de abajo
///    hacia arriba, sobre la pieza de más arriba de la llamada anterior (ver
///    docs/planes/2026-10-17-diagnostico-traslapos-variables.md).
```

- [ ] **Step 7: Compilar**

Run: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027`
Expected: errores **solo** en `Logica/GeneradorLongitudinal.cs` (usa el `Pedido` y el `Resolver` viejos); los arregla Task 7. Ningún error en otros archivos. Task 6 y Task 7 se revisan juntas al final de Task 7.

---

### Task 7: `GeneradorLongitudinal` -- anillo A/B, plan compartido con la ventana

**Files:**
- Modify: `Logica/GeneradorLongitudinal.cs`

**Interfaces:**
- Consumes: `PlanTraslapos.Calcular/Columna/Opciones/Resultado/PlanTipo/Traslapo` (Task 2), `ConfigLongitudinal.TipoBarraANombre/TipoBarraBNombre/RedondeoVarillas/SolapeMayorDeLosDos/MigrarBarrasAB()` (Task 3), `TiposEmpalme.Resolver(RebarBarType, double)` y `BarraContinua.TraslapoPedido/Pedido` (Task 6), `EstandarCQ.DiametroMm/APies`.
- Produces: la generación final.

- [ ] **Step 1: Barras A y B.** Reemplazar desde `if (string.IsNullOrEmpty(cfg.TipoBarraEsquinasNombre) ||` hasta la línea de `RebarBarType? barraCaraY = ResolverBarraOpcional(...)` (líneas 143-153) por:

```csharp
        // Una configuración guardada antes de los tipos A/B puede llegar sin
        // ellos (la ventana ya la migra al cargarla; esto es por si acaso).
        cfg.MigrarBarrasAB();

        RebarBarType? barraA = BarraPorNombre(cfg.TipoBarraANombre);
        RebarBarType? barraB = BarraPorNombre(cfg.TipoBarraBNombre);
        if (barraA is null || barraB is null)
        {
            res.Avisos.Add(
                $"{contexto.Marca}: el tipo de barra A «{cfg.TipoBarraANombre}» o el B «{cfg.TipoBarraBNombre}» " +
                "no existe en el documento; se omite el acero longitudinal de esta columna.");
            return;
        }

        RebarBarType? BarraPorNombre(string? nombre) =>
            !string.IsNullOrEmpty(nombre) && barrasPorNombre.TryGetValue(nombre, out RebarBarType? b) ? b : null;
```

- [ ] **Step 2: El plan, antes de crear nada.** Justo después del bloque del gancho de anclaje (el `if (contexto.IdZapata is not null) { ... }`), agregar:

```csharp
        // El mismo cálculo que dibuja la pestaña «Traslapos» de la ventana:
        // lo que se vio es lo que se genera. Sin solución, la columna no lleva
        // longitudinal, ni grapas ni cajas (abrazarían barras que no están) --
        // docs/planes/2026-10-17-diseno-metodologia-traslapos.md, sección 6.
        PlanTraslapos.Resultado planTraslapos = PlanTraslapos.Calcular(
            new PlanTraslapos.Columna(contexto.Tramos, contexto.ZInferiorLongitudinal, contexto.ZSuperior, ganchoLongitudinal is not null),
            EstandarCQ.DiametroMm(barraA), EstandarCQ.DiametroMm(barraB),
            new PlanTraslapos.Opciones(cfg.LongitudComercialBarra, cfg.RedondeoVarillas, cfg.SolapeMayorDeLosDos));
        if (!planTraslapos.TieneSolucion)
        {
            string motivos = string.Join(" ", new[] { planTraslapos.A.Motivo, planTraslapos.B.Motivo }.OfType<string>().Distinct());
            res.Avisos.Add(
                $"{contexto.Marca}: no se genera el acero longitudinal (ni sus grapas y cajas) porque los traslapos no tienen solución. {motivos}");
            return;
        }
```

- [ ] **Step 3: Insets.** Reemplazar

```csharp
        double insetEsquinas = recubrimientoPies + diametroEstribo + barraEsquinas.BarNominalDiameter / 2;
```

por

```csharp
        double Inset(RebarBarType barra) => recubrimientoPies + diametroEstribo + barra.BarNominalDiameter / 2;

        // Las esquinas "de referencia" (para repartir las intermedias, los
        // límites del corrimiento y las grapas/cajas) usan la más gruesa de
        // las dos barras: con A y B de distinto diámetro la diferencia es de
        // unos milímetros, y así ninguna queda pisando el estribo. Cada barra
        // se separa de su cara según SU diámetro (ver el anillo, más abajo).
        RebarBarType barraGruesa = barraA.BarNominalDiameter >= barraB.BarNominalDiameter ? barraA : barraB;
        double insetEsquinas = Inset(barraGruesa);
```

y reemplazar las dos líneas `double insetX = barraCaraX is not null ? ...` y `double insetY = barraCaraY is not null ? ...` por:

```csharp
        double insetX = insetEsquinas;
        double insetY = insetEsquinas;
```

- [ ] **Step 4: Comprobación de la zona común.** En el aviso de zona demasiado chica, reemplazar `la barra de esquina «{barraEsquinas.Name}»` por `la barra «{barraGruesa.Name}»`. Borrar los dos bloques `if (barraCaraX is not null && (xMax - xMin) - 2 * insetX <= Tolerancia) ...` y `if (barraCaraY is not null && (yMax - yMin) - 2 * insetY <= Tolerancia) ...`: con `insetX == insetY == insetEsquinas` los cubre el aviso anterior.

- [ ] **Step 5: Traza y alturas viejas.** En la traza de la columna, cambiar `longitud comercial {cfg.LongitudComercialBarra:0.#} m` por `longitud comercial {cfg.LongitudComercialBarra:0.#} m, redondeo {cfg.RedondeoVarillas:0.00} m`. Borrar `double zInicioTotal = ...` con su comentario, el bloque de `niveles` con su comentario, y `double zFinTotal = contexto.ZSuperior;`.

- [ ] **Step 6: El anillo con A y B.** Reemplazar desde `XYZ NormalDeEsquina(double y) => ...` hasta la última línea `for (int f = filas - 1; f >= 0; f--) AgregarDeCaraY(...)` por:

```csharp
        // A y B: "uno sí, uno no" dando la vuelta, A en las posiciones pares
        // del anillo (la esquina inferior izquierda es A). Es la misma
        // alternancia por anillo del 11-sep; ahora además decide la barra.
        RebarBarType BarraDe(int posicion) => posicion % 2 == 0 ? barraA : barraB;

        void AgregarEsquina(bool izquierda, bool abajo)
        {
            RebarBarType barra = BarraDe(anillo.Count);
            double inset = Inset(barra);
            double x = izquierda ? referencia.XMin + inset : referencia.XMax - inset;
            double y = abajo ? referencia.YMin + inset : referencia.YMax - inset;
            anillo.Add((barra, x, y, abajo ? XYZ.BasisY : XYZ.BasisX));
        }

        // Intermedias de las caras inferior/superior: varían en X.
        void AgregarDeCaraX(int c, bool abajo)
        {
            RebarBarType barra = BarraDe(anillo.Count);
            double x = PosicionIntermedia(xEsqIzq, xEsqDer, columnas, c);
            double y = abajo ? referencia.YMin + Inset(barra) : referencia.YMax - Inset(barra);
            anillo.Add((barra, x, y, XYZ.BasisX));
        }

        // Intermedias de las caras izquierda/derecha: varían en Y.
        void AgregarDeCaraY(int f, bool izquierda)
        {
            RebarBarType barra = BarraDe(anillo.Count);
            double y = PosicionIntermedia(yEsqAbajo, yEsqArriba, filas, f);
            double x = izquierda ? referencia.XMin + Inset(barra) : referencia.XMax - Inset(barra);
            anillo.Add((barra, x, y, XYZ.BasisY));
        }

        AgregarEsquina(izquierda: true, abajo: true);
        for (int c = 0; c < columnas; c++) AgregarDeCaraX(c, abajo: true);
        AgregarEsquina(izquierda: false, abajo: true);
        for (int f = 0; f < filas; f++) AgregarDeCaraY(f, izquierda: false);
        AgregarEsquina(izquierda: false, abajo: false);
        for (int c = columnas - 1; c >= 0; c--) AgregarDeCaraX(c, abajo: false);
        AgregarEsquina(izquierda: true, abajo: false);
        for (int f = filas - 1; f >= 0; f--) AgregarDeCaraY(f, izquierda: true);
```

En el comentario largo de arriba (el que explica las normales), cambiar la última viñeta "esquinas: ..." para que diga que la normal de las esquinas de abajo es Y y la de las de arriba es X, en vez de mencionar `NormalDeEsquina`.

- [ ] **Step 7: El bucle de creación.** Borrar `var alturasPorGrupo = new Dictionary<bool, List<double>>();` con su comentario. Dentro del `for (int i = 0; i < anillo.Count; i++)`, reemplazar desde `bool esParGrupo = i % 2 == 0;` hasta el cierre del `if (alturas.Count > 0) { ... }` del tipo de empalme por:

```csharp
            PlanTraslapos.PlanTipo plan = i % 2 == 0 ? planTraslapos.A : planTraslapos.B;

            // Un tipo de empalme por largo de traslapo; solo se crean los que
            // de verdad se usan.
            var traslapos = new List<BarraContinua.TraslapoPedido>();
            foreach (PlanTraslapos.Traslapo t in plan.Traslapos)
            {
                TiposEmpalme.Resultado tipo = tiposEmpalme.Resolver(barraDeLaPosicion, t.Largo);
                if (tipo.Aviso is not null) avisosDeBarras.Add(tipo.Aviso);
                if (tipo.Tipo is not null) traslapos.Add(new BarraContinua.TraslapoPedido(EstandarCQ.APies(t.Centro), tipo.Tipo));
            }
```

y reemplazar la llamada a `BarraContinua.Crear` por:

```csharp
            BarraContinua.Resultado creada = BarraContinua.Crear(doc, new BarraContinua.Pedido(
                barraDeLaPosicion, x, y, normal,
                EstandarCQ.APies(contexto.ZInferiorLongitudinal), EstandarCQ.APies(contexto.ZSuperior),
                traslapos, pila,
                ganchoLongitudinal, ganchoLongitudinal, orientacionAbajo, orientacionArriba,
                limites));
```

- [ ] **Step 8: Grapas y cajas.** En `GenerarMenores`, reemplazar las líneas de `barraCaraX`/`barraCaraY`/`barraMenoresRespaldo` (desde `barrasPorNombre.TryGetValue(cfg.TipoBarraCaraXNombre ?? "", ...` hasta `RebarBarType? barraMenoresRespaldo = barraCaraX ?? barraCaraY;`, comentario incluido) por:

```csharp
        barrasPorNombre.TryGetValue(cfg.TipoBarraANombre ?? "", out RebarBarType? barraA);

        // Las grapas solo existen donde hay barras intermedias en esa cara;
        // el resto del método usa estas dos como "hay intermedias" (antes eran
        // las barras de cara X/Y, que ya no se eligen por separado).
        RebarBarType? barraCaraX = columnas > 0 ? barraA : null;
        RebarBarType? barraCaraY = filas > 0 ? barraA : null;

        // Solo de RESPALDO: grapas y cajas se hacen con la barra del ESTRIBO
        // del tramo (ver más abajo). Esta queda para el caso de que el tramo
        // no tenga un tipo de barra de estribo válido.
        RebarBarType? barraMenoresRespaldo = barraA;
```

- [ ] **Step 9: Borrar lo que quedó sin uso:** `PlanificarAlturas`, `PlanificarSplicesReales`, `RedondearArriba10Cm` (con sus comentarios) y `ResolverBarraOpcional`.

Run: `rg -n "PlanificarAlturas|PlanificarSplicesReales|RedondearArriba10Cm|ResolverBarraOpcional|barraEsquinas|TipoBarraCara|TipoBarraEsquinas|TipoEmpalmeNombre|zInicioTotal|zFinTotal|esParGrupo|alturasPorGrupo" Logica`
Expected: sin resultados (salvo `TipoBarraCara`/`TipoBarraEsquinas` dentro de comentarios que expliquen la migración, si se dejó alguno).

- [ ] **Step 10: Cabecera de la clase.** En el comentario de `GeneradorLongitudinal` (líneas 19-36), reemplazar desde "Los traslapos solo se generan donde la longitud comercial..." hasta "...con el solape del cuadro de la empresa." por:

```csharp
///    Dónde va cada traslapo y cuánto mide cada varilla lo decide
///    <see cref="PlanTraslapos"/>, el mismo cálculo que dibuja la ventana
///    (docs/planes/2026-10-17-diseno-metodologia-traslapos.md): nunca en un
///    nudo, varillas en múltiplos del redondeo sin pasarse del largo
///    comercial, solape del cuadro como mínimo. Las posiciones se alternan
///    "una sí, una no" en el ANILLO del perímetro (ver
///    <see cref="GenerarColumna"/>, lista <c>anillo</c>): las pares son tipo A
///    y las impares tipo B, cada tipo con su barra, y traslapan en tramos
///    distintos -- regla del equipo, confirmada por Luciano el 11-sep (ver
///    docs/planes/2026-09-11-alternancia-de-traslapos-por-anillo.md). El
///    solape y el corrimiento lateral los pone Revit con un tipo de empalme
///    propio por largo (<see cref="TiposEmpalme"/>).
```

- [ ] **Step 11: Compilar las dos versiones**

Run: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027` y `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2026 -p:RevitInstallDir=Rev2026\`
Expected: `0 Errores` en las dos.

- [ ] **Step 12: Todo lo demás sigue compilando y pasando**

Run: `dotnet build tools/PreviewUI` y `dotnet run --project tools/PruebasTraslapos`
Expected: `0 Errores`; `0 fallas`.

---

### Task 8: Medir en Revit real, documentar y limpiar

**Files:**
- Create: `docs/planes/2026-10-17-retest-metodologia-traslapos.md`
- Delete (al final, si todo da bien): `ComandoDiagnosticoTraslaposVariables.cs` y su botón en `App.cs`

- [ ] **Step 1: Desplegar.** `Get-Process -Name Revit -ErrorAction SilentlyContinue` (vacío), después `powershell -ExecutionPolicy Bypass -File tools/deploy.ps1 -Versions 2027 -TargetFramework net10.0-windows`.

- [ ] **Step 2: Pedirle a Luciano el retest.** Mensaje, en lenguaje llano, con esta lista. Para cada punto, pedir captura o el número que se ve:
  1. Columna de la pila de referencia (dado 781598 + 782104 + 782735), redondeo 0,50, barra de 12 m: anotar lo que muestra la pestaña «Traslapos» para A y B (largos de varilla y traslapos). Generar.
  2. En Revit, seleccionar las varillas de una posición A y una B: el largo de barra que muestra Propiedades tiene que coincidir con la pestaña (±1 cm).
  3. Ningún traslapo dentro de una viga (vista 3D o corte).
  4. Cambiar a barra de 6 m y redondeo 1,00 en una columna alta hasta que algún tipo salga en rojo; generar: esa columna no tiene longitudinal ni grapas/cajas y el aviso lo explica (y sugiere 12 m si corresponde).
  5. Cargar una configuración guardada antes de este cambio: A/B llenos y el aviso.
  6. Regenerar una columna ya armada: no quedan piezas de la corrida anterior.
  7. Sin avisos de Revit al confirmar ("completely outside of its host").

- [ ] **Step 3: Documentar** en `docs/planes/2026-10-17-retest-metodologia-traslapos.md`: contexto, qué se midió, resultado de cada punto (sí/no y números), y qué queda pendiente. Si algún punto falla, **no** dar el cambio por terminado: describir el fallo y volver a planificar con Luciano.

- [ ] **Step 4: Limpiar.** Si los puntos 1 a 7 dan bien: borrar `ComandoDiagnosticoTraslaposVariables.cs` y su bloque en `App.cs`; compilar las dos versiones (`0 Errores`).

- [ ] **Step 5: Avisar a Luciano** qué quedó hecho, qué se midió, y que nada está commiteado; preguntar si quiere commit (y, aparte, si quiere push).
