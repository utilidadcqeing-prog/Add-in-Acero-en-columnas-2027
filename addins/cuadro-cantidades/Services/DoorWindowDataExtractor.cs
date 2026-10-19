using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Extrae los datos de texto del cuadro de puertas y ventanas: código, descripción, lugar en el piso,
/// medidas en plano y material. Todos salen de parámetros estándar del tipo o de las habitaciones
/// de Revit, sin depender del idioma de la plantilla.
/// </summary>
public static class DoorWindowDataExtractor
{
    public const string CodeKey = "Código";
    public const string DescriptionKey = "Descripción";
    public const string PlaceKey = "Lugar";
    public const string SizeKey = "Medidas";
    public const string MaterialKey = "Material";

    private static readonly CultureInfo SizeCulture = CultureInfo.GetCultureInfo("es-CO");

    public static bool IsDoor(Element element) =>
        element.Category?.Id.Value == (long)BuiltInCategory.OST_Doors;

    public static bool IsWindow(Element element) =>
        element.Category?.Id.Value == (long)BuiltInCategory.OST_Windows;

    /// <summary>
    /// Devuelve los datos de la puerta o ventana, o un diccionario vacío si el elemento no es ninguna de las dos.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetData(Element element)
    {
        bool isDoor = IsDoor(element);
        if ((!isDoor && !IsWindow(element)) || element is not FamilyInstance instance)
            return new Dictionary<string, string>();

        var type = instance.Symbol;
        var (widthParam, heightParam) = GetSizeParameters(isDoor);

        return new Dictionary<string, string>
        {
            [CodeKey] = GetText(type, BuiltInParameter.ALL_MODEL_TYPE_MARK),
            [DescriptionKey] = GetDescription(type),
            [PlaceKey] = GetPlace(instance),
            [SizeKey] = GetSize(instance, type, widthParam, heightParam),
            [MaterialKey] = GetMaterials(instance, type),
        };
    }

    /// <summary>
    /// Área de la puerta o ventana en m² (anchura × altura nominal de la familia), o 0 si el elemento
    /// no es ninguna de las dos o le falta alguna medida. Es la cantidad de las actividades en M2.
    /// </summary>
    public static double GetAreaSquareMeters(Element element)
    {
        bool isDoor = IsDoor(element);
        if ((!isDoor && !IsWindow(element)) || element is not FamilyInstance instance)
            return 0;

        var (widthParam, heightParam) = GetSizeParameters(isDoor);
        double width = GetLengthMeters(instance, instance.Symbol, widthParam);
        double height = GetLengthMeters(instance, instance.Symbol, heightParam);
        return width * height;
    }

    /// <summary>
    /// Agrega el área (m²) al diccionario de cantidades de la puerta o ventana. Reemplaza cualquier "Area"
    /// leído antes, porque el parámetro genérico de área no corresponde al vano de la familia.
    /// </summary>
    public static void AddQuantities(Element element, IDictionary<string, double> paramDict)
    {
        double area = GetAreaSquareMeters(element);
        if (area > 0)
            paramDict["Area"] = area;
    }

    private static (BuiltInParameter Width, BuiltInParameter Height) GetSizeParameters(bool isDoor) =>
        isDoor
            ? (BuiltInParameter.DOOR_WIDTH, BuiltInParameter.DOOR_HEIGHT)
            : (BuiltInParameter.WINDOW_WIDTH, BuiltInParameter.WINDOW_HEIGHT);

    private static string GetDescription(FamilySymbol type)
    {
        string description = GetText(type, BuiltInParameter.ALL_MODEL_DESCRIPTION);
        return description.Length > 0 ? description : type.Name;
    }

    /// <summary>
    /// Habitaciones a cada lado de la puerta o ventana. En fachada un lado queda sin habitación.
    /// </summary>
    private static string GetPlace(FamilyInstance instance)
    {
        var rooms = new[] { instance.FromRoom?.Name, instance.ToRoom?.Name }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .ToList();

        bool facade = instance.FromRoom == null || instance.ToRoom == null;
        return rooms.Count switch
        {
            0 => "Sin habitación",
            1 when facade => $"{rooms[0]} (fachada)",
            _ => string.Join(" / ", rooms),
        };
    }

    /// <summary>
    /// Anchura × altura en metros. El ejemplar tiene prioridad sobre el tipo.
    /// </summary>
    private static string GetSize(FamilyInstance instance, FamilySymbol type, BuiltInParameter widthParam, BuiltInParameter heightParam)
    {
        double width = GetLengthMeters(instance, type, widthParam);
        double height = GetLengthMeters(instance, type, heightParam);
        if (width <= 0 || height <= 0)
            return string.Empty;

        return $"{width.ToString("0.00", SizeCulture)} × {height.ToString("0.00", SizeCulture)}";
    }

    private static double GetLengthMeters(Element instance, Element type, BuiltInParameter bip)
    {
        foreach (var source in new[] { instance, type })
        {
            var param = source.get_Parameter(bip);
            if (param != null && param.HasValue && param.AsDouble() > 0)
                return UnitUtils.ConvertFromInternalUnits(param.AsDouble(), UnitTypeId.Meters);
        }
        return 0;
    }

    /// <summary>
    /// Materiales asignados en los parámetros de material del ejemplar y del tipo (marco, cristalera…).
    /// Si todos están en "Por categoría", se usan los materiales de la geometría.
    /// </summary>
    private static string GetMaterials(FamilyInstance instance, FamilySymbol type)
    {
        var doc = instance.Document;
        var materialIds = instance.Parameters.Cast<Parameter>()
            .Concat(type.Parameters.Cast<Parameter>())
            .Where(p => p.StorageType == StorageType.ElementId
                && p.Definition.GetDataType() == SpecTypeId.Reference.Material)
            .Select(p => p.AsElementId())
            .ToList();

        if (!materialIds.Any(id => id != ElementId.InvalidElementId))
            materialIds = instance.GetMaterialIds(false).ToList();

        var names = materialIds
            .Where(id => id != ElementId.InvalidElementId)
            .Select(id => (doc.GetElement(id) as Material)?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .OrderBy(name => name);

        // " + " y no coma: hay nombres de material con coma ("Cristal, cristalera transparente")
        return string.Join(" + ", names);
    }

    private static string GetText(Element element, BuiltInParameter bip) =>
        element.get_Parameter(bip)?.AsString()?.Trim() ?? string.Empty;
}
