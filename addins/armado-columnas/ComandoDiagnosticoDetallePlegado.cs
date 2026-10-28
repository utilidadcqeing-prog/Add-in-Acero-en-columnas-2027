namespace practicacq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using practicacq.Logica;

/// <summary>
/// TEMPORAL -- #176 (docs/planes/2026-10-27-medidas-ganchos-detalle-176.md):
/// lista los tipos de detalle de plegado del modelo con sus casillas de cotas
/// (la de los ganchos incluida), cuál es el tipo por defecto, y cada detalle
/// ya puesto con su tipo y la barra que representa (forma y ganchos). Sirve
/// para comparar el detalle de vigas, que sí acota los ganchos, con el de
/// columnas. Solo lee, no cambia nada. Sacar el comando (y su botón) una vez
/// cerrado el #176.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class ComandoDiagnosticoDetallePlegado : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document doc = commandData.Application.ActiveUIDocument.Document;
        var sb = new StringBuilder();

        List<RebarBendingDetailType> tipos = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBendingDetailType)).Cast<RebarBendingDetailType>()
            .OrderBy(t => t.Name).ToList();

        ElementId porDefecto = doc.GetDefaultElementTypeId(ElementTypeGroup.BendingDetail);
        ElementId porDefectoEsquematico = doc.GetDefaultElementTypeId(ElementTypeGroup.SchematicBendingDetail);
        sb.AppendLine($"Tipo por defecto (realista): {Nombre(doc, porDefecto)}");
        sb.AppendLine($"Tipo por defecto (esquemático): {Nombre(doc, porDefectoEsquematico)}");
        sb.AppendLine();

        sb.AppendLine($"TIPOS DE DETALLE DE PLEGADO ({tipos.Count})");
        foreach (RebarBendingDetailType t in tipos)
        {
            sb.AppendLine($"- «{t.Name}» (id {t.Id.Value})");
            sb.AppendLine($"    modo: {Leer(() => t.DisplayMode)}; cotas de segmento: {Leer(() => t.SegmentLengthDimensionsEnabled)}; " +
                          $"cotas de GANCHOS: {Leer(() => t.SegmentLengthDimensionsForHooksEnabled)}");
            sb.AppendLine($"    cotas como: {Leer(() => t.SegmentLengthsDisplayOption)}; etiqueta: {Leer(() => Nombre(doc, t.TagTypeId))}; " +
                          $"ángulos: {Leer(() => t.AngularDimensionsEnabled)} (ganchos: {Leer(() => t.AngularDimensionsForHooksEnabled)})");
        }
        sb.AppendLine();

        var idsTipos = new HashSet<ElementId>(tipos.Select(t => t.Id));
        List<Element> detalles = new FilteredElementCollector(doc).WhereElementIsNotElementType()
            .Where(e => idsTipos.Contains(e.GetTypeId())).ToList();

        sb.AppendLine($"DETALLES PUESTOS ({detalles.Count})");
        foreach (Element d in detalles)
        {
            string vista = d.OwnerViewId != ElementId.InvalidElementId ? Nombre(doc, d.OwnerViewId) : "?";
            sb.AppendLine($"- detalle {d.Id.Value} en «{vista}», tipo «{Nombre(doc, d.GetTypeId())}»");
            Element? barra = null;
            try
            {
                Reference? host = RebarBendingDetail.GetHost(d);
                if (host is not null) barra = doc.GetElement(host.ElementId);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    (no se pudo leer la barra: {ex.Message})");
            }
            if (barra is Rebar r)
            {
                sb.AppendLine($"    barra {r.Id.Value}: forma «{Nombre(doc, r.GetShapeId())}», tipo «{Nombre(doc, r.GetTypeId())}», " +
                              $"ganchos «{Leer(() => Nombre(doc, r.GetHookTypeId(0)))}» / «{Leer(() => Nombre(doc, r.GetHookTypeId(1)))}», " +
                              $"largo de gancho modificado: {Leer(() => r.IsHookLengthOverrideEnabled())}");
            }
            else if (barra is not null)
            {
                sb.AppendLine($"    representa {barra.GetType().Name} {barra.Id.Value}");
            }
        }
        sb.AppendLine();

        // Lo que no encuentra la búsqueda de arriba (o para comparar el
        // estribo de vigas con el de columnas): se eligen a mano detalles de
        // plegado y/o estribos.
        sb.AppendLine($"La forma define los ganchos (configuración del modelo): {Leer(() => ReinforcementSettings.GetReinforcementSettings(doc).RebarShapeDefinesHooks)}");
        IList<Reference> elegidos;
        try
        {
            elegidos = commandData.Application.ActiveUIDocument.Selection.PickObjects(ObjectType.Element,
                "Elige el estribo de vigas, el de columnas y/o sus detalles de plegado; después «Finalizar»");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            elegidos = new List<Reference>();
        }

        sb.AppendLine($"ELEGIDOS ({elegidos.Count})");
        foreach (Reference referencia in elegidos)
        {
            Element e = doc.GetElement(referencia);
            if (e is Rebar r)
            {
                DescribirBarra(doc, r, sb);
                continue;
            }
            sb.AppendLine($"- {e.GetType().Name} {e.Id.Value}, categoría «{e.Category?.Name}», tipo «{Nombre(doc, e.GetTypeId())}», " +
                          $"¿detalle de plegado? {Leer(() => RebarBendingDetail.IsBendingDetail(e))}");
            try
            {
                Reference? host = RebarBendingDetail.GetHost(e);
                if (host is not null && doc.GetElement(host.ElementId) is Rebar barraDelDetalle)
                    DescribirBarra(doc, barraDelDetalle, sb);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    (no se pudo leer la barra: {ex.Message})");
            }
        }

        DiagnosticoUI.MostrarTexto(sb.ToString(), "Diagnóstico detalle de plegado (#176)");
        return Result.Succeeded;
    }

    /// <summary>Forma, ganchos y parámetros de gancho de una barra, para comparar la de vigas con la de columnas.</summary>
    private static void DescribirBarra(Document doc, Rebar r, StringBuilder sb)
    {
        var forma = doc.GetElement(r.GetShapeId()) as RebarShape;
        string definicion = Leer(() => forma?.GetRebarShapeDefinition() is RebarShapeDefinitionBySegments s
            ? $"por segmentos ({s.NumberOfSegments})" : forma?.GetRebarShapeDefinition()?.GetType().Name ?? "?");
        sb.AppendLine($"- barra {r.Id.Value}: forma «{forma?.Name}» {definicion}, estilo {Leer(() => forma?.RebarStyle)}, " +
                      $"tipo «{Nombre(doc, r.GetTypeId())}», por forma: {Leer(() => r.IsRebarShapeDriven())}, " +
                      $"largo de gancho modificado: {Leer(() => r.IsHookLengthOverrideEnabled())}");
        // Los parámetros de la forma y sus fórmulas: en vigas, «T1» trae A y G
        // (0.08, lo mismo que acota el detalle en los ganchos); «CQ Estribo»
        // solo CQ A-D. Interesa qué fórmula los ata al gancho.
        if (forma?.GetRebarShapeDefinition() is RebarShapeDefinition def)
        {
            foreach (ElementId idParam in def.GetParameters())
            {
                string formula = Leer(() => def.GetParameterFormula(idParam));
                sb.AppendLine($"    forma: parámetro «{Nombre(doc, idParam)}» (id {idParam.Value}) por defecto " +
                              $"{Leer(() => GeometriaSeccion.Metros(def.GetParameterDefaultValue(idParam)).ToString("0.####"))}" +
                              (string.IsNullOrEmpty(formula) ? "" : $", fórmula «{formula}»"));
            }
        }

        var tipoBarra = doc.GetElement(r.GetTypeId()) as RebarBarType;
        for (int punta = 0; punta < 2; punta++)
        {
            int p = punta;
            ElementId idGancho = r.GetHookTypeId(p);
            if (doc.GetElement(idGancho) is RebarHookType gancho)
                sb.AppendLine($"    gancho {p}: «{gancho.Name}», {Leer(() => gancho.HookAngle * 180 / Math.PI):0}°, estilo {Leer(() => gancho.Style)}, " +
                              $"largo según la barra {Leer(() => GeometriaSeccion.Metros(tipoBarra!.GetHookLength(idGancho)).ToString("0.####"))}, " +
                              $"orientación {Leer(() => r.GetTerminationOrientation(p))}");
            else
                sb.AppendLine($"    gancho {p}: (ninguno)");
        }
        foreach (Parameter parametro in r.Parameters)
        {
            string nombre = parametro.Definition?.Name ?? "";
            if (nombre.IndexOf("ancho", StringComparison.OrdinalIgnoreCase) < 0 && nombre.IndexOf("hook", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            sb.AppendLine($"    parámetro «{nombre}» = {Leer(() => parametro.AsValueString() ?? parametro.AsString() ?? parametro.AsInteger().ToString())}");
        }
    }

    private static string Nombre(Document doc, ElementId id) =>
        id == ElementId.InvalidElementId ? "(ninguno)" : doc.GetElement(id)?.Name ?? $"(id {id.Value})";

    private static string Leer<T>(Func<T> leer)
    {
        try { return leer()?.ToString() ?? "null"; }
        catch (Exception ex) { return $"error: {ex.Message}"; }
    }
}
