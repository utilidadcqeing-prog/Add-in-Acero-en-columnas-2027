using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Nombre del nivel de un elemento para el JSON y la tabla de detalles. Los elementos hospedados sin nivel propio
/// (barras, refuerzos de área) toman el de su anfitrión.
/// </summary>
public static class ElementLevelReader
{
    /// <summary>
    /// Texto que se guarda cuando el elemento no tiene nivel.
    /// </summary>
    public const string NoLevel = "Sin Nivel";

    /// <summary>
    /// Nombre del nivel del elemento o, si no tiene, el de su anfitrión; <see cref="NoLevel"/> si no se encuentra.
    /// </summary>
    public static string GetLevelName(Element? element)
    {
        if (element == null || !element.IsValidObject)
            return NoLevel;

        var doc = element.Document;

        // El anfitrión manda en barras y refuerzos de área: su nivel propio no existe o no es el de la losa
        var hostId = element is AreaReinforcement areaReinforcement
            ? areaReinforcement.GetHostId()
            : RebarQuantityExtractor.GetHostId(element);
        if (hostId != ElementId.InvalidElementId && doc.GetElement(hostId) is { } host)
            return GetLevelName(host);

        if (doc.GetElement(element.LevelId) is Level level)
            return level.Name;

        // Parámetros comunes de nivel
        BuiltInParameter[] bips =
        {
            BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
            BuiltInParameter.FAMILY_BASE_LEVEL_PARAM,
            BuiltInParameter.WALL_BASE_CONSTRAINT,
            BuiltInParameter.LEVEL_PARAM,
            BuiltInParameter.SCHEDULE_LEVEL_PARAM,
        };

        foreach (var bip in bips)
        {
            if (LevelFrom(doc, element.get_Parameter(bip)) is { } name)
                return name;
        }

        // Por nombre de parámetro (incluye Restricción de base de muros)
        string[] nameLookups =
        {
            "Base Constraint", "Restricción de base", "Nivel", "Level", "Nivel de base", "Base Level",
            "Nivel de referencia", "Reference Level",
        };

        foreach (var name in nameLookups)
        {
            if (LevelFrom(doc, element.LookupParameter(name)) is { } levelName)
                return levelName;
        }

        return NoLevel;
    }

    private static string? LevelFrom(Document doc, Parameter? parameter) =>
        parameter is { HasValue: true, StorageType: StorageType.ElementId }
        && doc.GetElement(parameter.AsElementId()) is Level level
            ? level.Name
            : null;
}
