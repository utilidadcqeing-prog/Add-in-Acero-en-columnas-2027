namespace practicacq;

using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

/// <summary>
/// TEMPORAL -- comando de diagnóstico, no forma parte del flujo del add-in.
/// Sirve para medir a mano las coordenadas reales de barras longitudinales
/// (por ejemplo, dos barras de un mismo empalme en el modelo de referencia de
/// William) y así confirmar el corrimiento lateral real del traslapo antes de
/// programarlo a ciegas en <see cref="Logica.GeneradorLongitudinal"/>. Sacar
/// una vez que se confirme ese dato.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class ComandoDiagnosticoRebar : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        IList<Reference> refs;
        try
        {
            refs = uidoc.Selection.PickObjects(
                ObjectType.Element, new FiltroSoloRebar(),
                "Selecciona una o más barras (Rebar) para ver sus coordenadas -- Finish (Enter) para terminar.");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }

        var sb = new StringBuilder();
        foreach (Reference r in refs)
        {
            if (doc.GetElement(r) is not Rebar rebar) continue;

            sb.AppendLine($"--- Rebar {rebar.Id.Value} (host {rebar.GetHostId().Value}) ---");

            int posiciones;
            try { posiciones = rebar.NumberOfBarPositions; }
            catch { posiciones = 1; }

            for (int pos = 0; pos < Math.Max(1, posiciones); pos++)
            {
                IList<Curve>? curvas;
                try
                {
                    curvas = rebar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, pos);
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  [posición {pos}] no se pudo leer geometría: {ex.Message}");
                    continue;
                }

                sb.AppendLine($"  [posición {pos}]");
                foreach (Curve c in curvas)
                    sb.AppendLine($"    {Fmt(c.GetEndPoint(0))} -> {Fmt(c.GetEndPoint(1))}");
            }
        }

        MessageBox.Show(sb.ToString(), "Diagnóstico Rebar (coordenadas en metros)");
        return Result.Succeeded;
    }

    private static string Fmt(XYZ p) =>
        $"({Metros(p.X):0.000}, {Metros(p.Y):0.000}, {Metros(p.Z):0.000})";

    private static double Metros(double pies) => UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Meters);
}

internal sealed class FiltroSoloRebar : ISelectionFilter
{
    public bool AllowElement(Element elem) => elem is Rebar;
    public bool AllowReference(Reference reference, XYZ position) => true;
}
