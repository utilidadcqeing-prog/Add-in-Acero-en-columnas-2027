namespace practicacq;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using practicacq.Logica;
using practicacq.Modelo;

/// <summary>
/// TEMPORAL -- mide contra Revit real cómo quedan las grapas con gancho de
/// 135° y 90° antes de programar el generador (regla del CLAUDE.md). Plan:
/// docs/planes/2026-10-26-plan-grapas-angulo-por-punta.md, Tarea 1;
/// resultados en docs/planes/2026-10-26-diagnostico-ganchos-grapa.md.
///
/// Se elige una columna y se crean, cada una en su transacción y a otra
/// altura, grapas #3 de lado a lado en X con distintas combinaciones de
/// gancho y forma. De cada una informa si se creó, con qué forma quedó y,
/// por punta, el arco del gancho (centro, radio, ángulo barrido) y la cola,
/// con el Axial/Lateral del centro respecto de la punta pedida (como
/// GeneradorLongitudinal.MedirGanchoDeGrapa). Al final pregunta si deja las
/// grapas para mirarlas en 3D. Sacar una vez cerrado el plan.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ComandoDiagnosticoGanchosGrapa : IExternalCommand
{
    private const string Titulo = "Diagnóstico ganchos de grapa (135° / 90°)";

    private sealed record Caso(
        string Nombre, FormaMenor Forma, AnguloGancho FormaInicio, AnguloGancho FormaFin,
        AnguloGancho GanchoInicio, AnguloGancho GanchoFin, bool SinForma = false);

    private static readonly Caso[] Casos =
    {
        new("1. C 180/180, forma 180/180 (control)", FormaMenor.GrapaC, AnguloGancho.Grados180, AnguloGancho.Grados180, AnguloGancho.Grados180, AnguloGancho.Grados180),
        new("2. C ganchos 135/135 con la forma 180/180", FormaMenor.GrapaC, AnguloGancho.Grados180, AnguloGancho.Grados180, AnguloGancho.Grados135, AnguloGancho.Grados135),
        new("3. C 135/135, forma 135-135", FormaMenor.GrapaC, AnguloGancho.Grados135, AnguloGancho.Grados135, AnguloGancho.Grados135, AnguloGancho.Grados135),
        new("4. C 90/90, forma 90-90", FormaMenor.GrapaC, AnguloGancho.Grados90, AnguloGancho.Grados90, AnguloGancho.Grados90, AnguloGancho.Grados90),
        new("5. C 135/90, forma 135-90", FormaMenor.GrapaC, AnguloGancho.Grados135, AnguloGancho.Grados90, AnguloGancho.Grados135, AnguloGancho.Grados90),
        new("6. S 135/180, forma S 135-180", FormaMenor.GrapaS, AnguloGancho.Grados135, AnguloGancho.Grados180, AnguloGancho.Grados135, AnguloGancho.Grados180),
        new("7. C 135/135 con CreateFromCurves (Revit elige la forma)", FormaMenor.GrapaC, AnguloGancho.Grados135, AnguloGancho.Grados135, AnguloGancho.Grados135, AnguloGancho.Grados135, SinForma: true),
    };

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        FamilyInstance? columna;
        try
        {
            Reference r = uidoc.Selection.PickObject(ObjectType.Element, new FiltroColumna(), "Selecciona una columna");
            columna = doc.GetElement(r) as FamilyInstance;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }

        if (columna?.get_BoundingBox(null) is not BoundingBoxXYZ caja)
        {
            TaskDialog.Show(Titulo, "La columna no tiene geometría.");
            return Result.Failed;
        }

        RebarBarType? barra = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
            .OrderBy(b => b.Name == "#3 CQEING" ? 0 : 1)
            .ThenBy(b => Math.Abs(b.BarNominalDiameter - EstandarCQ.APies(0.0095)))
            .FirstOrDefault();
        if (barra is null)
        {
            TaskDialog.Show(Titulo, "No hay ningún tipo de barra en el modelo.");
            return Result.Failed;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Columna {columna.Id.Value}, barra «{barra.Name}» ({M(barra.BarNominalDiameter) * 1000:0.0} mm, " +
                      $"doblado de estribo {M(barra.StirrupTieBendDiameter) * 1000:0.0} mm).");
        sb.AppendLine($"RebarShapeDefinesHooks = {ReinforcementSettings.GetReinforcementSettings(doc).RebarShapeDefinesHooks}.");
        sb.AppendLine("Pies y metros: todo en metros. Lateral > 0 = a la izquierda de desde→hasta (hacia +Y).");
        sb.AppendLine();

        // Línea de lado a lado en X por el medio, 6 cm adentro de cada cara.
        double inset = EstandarCQ.APies(0.06);
        double yMedio = (caja.Min.Y + caja.Max.Y) / 2;
        double alto = caja.Max.Z - caja.Min.Z;

        using var grupo = new TransactionGroup(doc, "Diagnóstico ganchos de grapa");
        grupo.Start();

        var propios = new TiposPropios(doc);
        for (int i = 0; i < Casos.Length; i++)
        {
            Caso caso = Casos[i];
            double z = caja.Min.Z + alto * (i + 1) / (Casos.Length + 1);
            var desde = new XYZ(caja.Min.X + inset, yMedio, z);
            var hasta = new XYZ(caja.Max.X - inset, yMedio, z);

            sb.AppendLine($"=== {caso.Nombre} (Z = {M(z):0.00} m)");
            using var t = new Transaction(doc, caso.Nombre);
            t.Start();
            try
            {
                Probar(doc, propios, columna, barra, caso, desde, hasta, sb);
                t.Commit();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  ERROR: {ex.GetType().Name}: {ex.Message}");
                if (t.HasStarted() && !t.HasEnded()) t.RollBack();
            }

            sb.AppendLine();
        }

        string texto = sb.ToString();
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "practicacq-diagnostico-ganchos-grapa.txt"), texto); }
        catch { /* el informe igual sale en la ventana */ }
        DiagnosticoUI.MostrarTexto(texto, Titulo);

        TaskDialogResult dejar = TaskDialog.Show(Titulo,
            "¿Dejar las grapas de prueba en el modelo para mirarlas en 3D? (Ctrl+Z las saca)",
            TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
        if (dejar == TaskDialogResult.Yes) grupo.Assimilate();
        else grupo.RollBack();
        return Result.Succeeded;
    }

    private static void Probar(
        Document doc, TiposPropios propios, FamilyInstance columna, RebarBarType barra, Caso caso,
        XYZ desde, XYZ hasta, StringBuilder sb)
    {
        TiposPropios.Pieza<RebarHookType> gInicio = propios.GanchoGrapaEstribo(caso.GanchoInicio);
        TiposPropios.Pieza<RebarHookType> gFin = propios.GanchoGrapaEstribo(caso.GanchoFin);
        if (gInicio.Tipo is null || gFin.Tipo is null)
        {
            sb.AppendLine($"  Sin gancho: {gInicio.Aviso ?? gFin.Aviso}");
            return;
        }

        foreach (RebarHookType g in new[] { gInicio.Tipo, gFin.Tipo }.Distinct())
            sb.AppendLine($"  Gancho «{g.Name}»: {g.HookAngle * 180 / Math.PI:0}°, estilo {g.Style}, " +
                          $"extensión {M(g.GetHookExtensionLength(barra)) * 100:0.00} cm con esta barra.");

        // C: los dos a la izquierda; S: el del fin a la derecha (diagnóstico del 20-oct).
        using var terminaciones = new BarTerminationsData(doc)
        {
            HookTypeIdAtStart = gInicio.Tipo.Id,
            HookTypeIdAtEnd = gFin.Tipo.Id,
            TerminationOrientationAtStart = RebarTerminationOrientation.Left,
            TerminationOrientationAtEnd = caso.Forma == FormaMenor.GrapaS ? RebarTerminationOrientation.Right : RebarTerminationOrientation.Left,
        };
        var curvas = new List<Curve> { Line.CreateBound(desde, hasta) };

        Rebar? grapa;
        if (caso.SinForma)
        {
            grapa = Rebar.CreateFromCurves(doc, RebarStyle.StirrupTie, barra, columna, XYZ.BasisZ, curvas, terminaciones,
                useExistingShapeIfPossible: true, createNewShape: true);
        }
        else
        {
            TiposPropios.Pieza<RebarShape> forma = propios.FormaGrapaEstribo(caso.Forma, caso.FormaInicio, caso.FormaFin);
            if (forma.Tipo is null)
            {
                sb.AppendLine($"  Sin forma: {forma.Aviso}");
                return;
            }

            sb.AppendLine($"  Forma pedida «{forma.Tipo.Name}».");
            grapa = Rebar.CreateFromCurvesAndShape(doc, forma.Tipo, barra, columna, XYZ.BasisZ, curvas, terminaciones);
        }

        if (grapa is null)
        {
            sb.AppendLine("  Revit devolvió null.");
            return;
        }

        doc.Regenerate();
        string formaFinal = doc.GetElement(grapa.GetShapeId()) is RebarShape f ? f.Name : "(sin forma)";
        sb.AppendLine($"  Creada (Id {grapa.Id.Value}) con la forma «{formaFinal}».");
        for (int punta = 0; punta <= 1; punta++)
            sb.AppendLine($"  Punta {(punta == 0 ? "inicio" : "fin")}: gancho «{doc.GetElement(grapa.GetHookTypeId(punta))?.Name ?? "(ninguno)"}», " +
                          $"orientación {grapa.GetTerminationOrientation(punta)}.");

        IList<Curve> eje = grapa.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, 0);
        sb.AppendLine("  Curvas (eje, con ganchos):");
        foreach (Curve c in eje)
        {
            XYZ a = c.GetEndPoint(0), b = c.GetEndPoint(1);
            string extra = c is Arc arco
                ? $" centro ({M(arco.Center.X):0.0000}; {M(arco.Center.Y):0.0000}) radio {M(arco.Radius) * 1000:0.0} mm, barre {c.Length / arco.Radius * 180 / Math.PI:0.0}°"
                : "";
            sb.AppendLine($"    {(c is Arc ? "Arco " : "Recta")} ({M(a.X):0.0000}; {M(a.Y):0.0000}) → ({M(b.X):0.0000}; {M(b.Y):0.0000}), " +
                          $"largo {M(c.Length) * 100:0.00} cm{extra}");
        }

        // El tramo principal es la recta más larga; lo de antes es el gancho
        // del inicio y lo de después, el del fin.
        int principal = Enumerable.Range(0, eje.Count).Where(k => eje[k] is Line).OrderByDescending(k => eje[k].Length).FirstOrDefault();
        XYZ u = (hasta - desde).Normalize(), izquierda = XYZ.BasisZ.CrossProduct(u);
        Informar("inicio", eje.Take(principal).Reverse().ToList(), desde, u, izquierda, sb);
        Informar("fin", eje.Skip(principal + 1).ToList(), hasta, -u, izquierda, sb);
    }

    /// <param name="gancho">Las curvas del gancho desde el tramo principal hacia la punta: arco y cola.</param>
    /// <param name="haciaAdentro">Desde esta punta hacia la otra.</param>
    private static void Informar(string punta, IList<Curve> gancho, XYZ pedida, XYZ haciaAdentro, XYZ izquierda, StringBuilder sb)
    {
        if (gancho.FirstOrDefault(c => c is Arc) is not Arc arco)
        {
            sb.AppendLine($"  {punta}: sin arco de gancho.");
            return;
        }

        XYZ d = arco.Center - pedida;
        sb.AppendLine($"  {punta}: centro del doblez a {M(d.DotProduct(haciaAdentro)) * 100:0.00} cm hacia adentro (Axial) y " +
                      $"{M(d.DotProduct(izquierda)) * 100:0.00} cm de costado (Lateral) de la punta pedida; " +
                      $"barre {arco.Length / arco.Radius * 180 / Math.PI:0.0}°.");
        if (gancho.LastOrDefault() is Line cola && !ReferenceEquals(cola, arco))
        {
            // De la punta del arco hacia la punta libre de la cola (en el
            // inicio Revit la recorre al revés).
            XYZ a = cola.GetEndPoint(0), b = cola.GetEndPoint(1);
            XYZ dir = (a.DistanceTo(arco.Center) < b.DistanceTo(arco.Center) ? b - a : a - b).Normalize();
            sb.AppendLine($"  {punta}: cola de {M(cola.Length) * 100:0.00} cm, dirección " +
                          $"{dir.DotProduct(haciaAdentro):0.000} hacia adentro / {dir.DotProduct(izquierda):0.000} a la izquierda.");
        }
    }

    private static double M(double pies) => GeometriaSeccion.Metros(pies);

    private sealed class FiltroColumna : ISelectionFilter
    {
        public bool AllowElement(Element elem) =>
            elem.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
