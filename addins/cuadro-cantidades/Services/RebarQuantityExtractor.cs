using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Extrae cantidades y anfitrión de barras de acero de Revit.
/// Cubre tanto barras independientes (<see cref="Rebar"/>) como las barras generadas por
/// refuerzos de área o por camino (<see cref="RebarInSystem"/>), que comparten categoría
/// (OST_Rebar) pero no heredan de <see cref="Rebar"/>.
/// </summary>
public static class RebarQuantityExtractor
{
    /// <summary>
    /// Indica si el elemento es una barra de acero cuantificable (Rebar o RebarInSystem).
    /// </summary>
    public static bool IsRebarBar(Element element) => element is Rebar || element is RebarInSystem;

    /// <summary>
    /// Obtiene el anfitrión de la barra. Devuelve <see cref="ElementId.InvalidElementId"/> si no es una barra
    /// o si Revit no reporta anfitrión (ocurre en modelos colaborativos o con ciertas plantillas).
    /// </summary>
    public static ElementId GetHostId(Element element) => element switch
    {
        Rebar rebar => rebar.GetHostId(),
        RebarInSystem rebarInSystem => rebarInSystem.GetHostId(),
        _ => ElementId.InvalidElementId,
    };

    /// <summary>
    /// Devuelve el nombre del TIPO del anfitrión (no de la instancia) para garantizar un valor
    /// consistente entre distintas instalaciones y plantillas; si el tipo no tiene nombre, usa el de la instancia.
    /// </summary>
    public static string? GetHostName(Element element)
    {
        var hostId = GetHostId(element);
        if (hostId == ElementId.InvalidElementId)
            return null;

        var doc = element.Document;
        var hostEl = doc.GetElement(hostId);
        if (hostEl == null)
            return null;

        var hostType = doc.GetElement(hostEl.GetTypeId());
        return hostType != null && !string.IsNullOrWhiteSpace(hostType.Name) ? hostType.Name : hostEl.Name;
    }

    /// <summary>
    /// Agrega Length (m), kg/m y Peso (kg) al diccionario de parámetros si el elemento es una barra.
    /// Revit no almacena el peso total como BuiltInParameter de instancia, por lo que se calcula como
    /// Total Bar Length * Bar Weight, igual que las tablas de planificación de Autodesk.
    /// </summary>
    public static void AddQuantities(Element element, IDictionary<string, double> paramDict)
    {
        double? totalLengthFt = element switch
        {
            Rebar rebar => rebar.TotalLength,
            RebarInSystem rebarInSystem => rebarInSystem.TotalLength,
            _ => null,
        };
        if (totalLengthFt == null)
            return;

        double totalLengthM = UnitUtils.ConvertFromInternalUnits(totalLengthFt.Value, UnitTypeId.Meters);
        double kgm = GetBarMassPerMeter(element);

        paramDict["Length"] = totalLengthM;
        paramDict["kg/m"] = kgm;
        paramDict["Peso"] = totalLengthM * kgm;
    }

    private static double GetBarMassPerMeter(Element element)
    {
        if (element.Document.GetElement(element.GetTypeId()) is not RebarBarType rebarType)
            return 0;

        // Buscar el parámetro por su tipo de dato oficial (ForgeTypeId): independiente del idioma
        double kgm = MassPerLengthReader.FindKilogramsPerMeter(rebarType);
        if (kgm > 0)
            return kgm;

        // Propiedad directa añadida en las versiones más recientes de la API (fallback)
        var propInfo = rebarType.GetType().GetProperty("BarMassPerUnitLength");
        if (propInfo?.GetValue(rebarType) is double dVal)
            return UnitUtils.ConvertFromInternalUnits(dVal, UnitTypeId.KilogramsPerMeter);

        return 0;
    }
}
