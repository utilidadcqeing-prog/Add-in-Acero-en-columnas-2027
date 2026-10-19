using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Extrae longitud, peso lineal (kg/m) y peso (kg) de perfiles metálicos de armazón y pilares estructurales.
/// El peso lineal sale del parámetro de masa por unidad de longitud del elemento o de su tipo
/// (p. ej. el parámetro compartido "Peso lineal metálico"); el peso es longitud × peso lineal.
/// No se calcula por área transversal × densidad porque Revit no expone el área exacta de la sección.
/// </summary>
public static class SteelProfileQuantityExtractor
{
    /// <summary>
    /// Indica si el elemento pertenece a una categoría de perfil cuantificable por peso lineal.
    /// </summary>
    public static bool IsProfileCategory(Element element) =>
        element.Category?.Id.Value
            is (long)BuiltInCategory.OST_StructuralFraming
                or (long)BuiltInCategory.OST_StructuralColumns;

    /// <summary>
    /// Agrega kg/m y Peso (kg) al diccionario si el elemento es un perfil con peso lineal asignado.
    /// Reutiliza la longitud ya extraída (en metros) para que LONGITUD y PESO sean coherentes; si falta, la calcula.
    /// Los elementos sin peso lineal (p. ej. columnas de concreto) no se modifican.
    /// </summary>
    public static void AddQuantities(Element element, IDictionary<string, double> paramDict)
    {
        if (!IsProfileCategory(element))
            return;

        double kgm = GetKilogramsPerMeter(element);
        if (kgm <= 0)
            return;

        if (!paramDict.TryGetValue("Length", out double lengthM) || lengthM <= 0)
        {
            lengthM = GetLengthMeters(element);
            if (lengthM > 0)
                paramDict["Length"] = lengthM;
        }

        paramDict["kg/m"] = kgm;
        paramDict["Peso"] = lengthM > 0 ? lengthM * kgm : 0;
    }

    private static double GetKilogramsPerMeter(Element element)
    {
        // El parámetro de instancia tiene prioridad sobre el de tipo
        double kgm = MassPerLengthReader.FindKilogramsPerMeter(element);
        if (kgm > 0)
            return kgm;

        return element.Document.GetElement(element.GetTypeId()) is ElementType type
            ? MassPerLengthReader.FindKilogramsPerMeter(type)
            : 0;
    }

    private static double GetLengthMeters(Element element)
    {
        Parameter? lengthParam =
            element.get_Parameter(BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH)
            ?? element.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM)
            ?? element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);

        if (lengthParam != null && lengthParam.HasValue && lengthParam.AsDouble() > 0)
            return UnitUtils.ConvertFromInternalUnits(lengthParam.AsDouble(), UnitTypeId.Meters);

        if (element.Location is LocationCurve locationCurve)
            return UnitUtils.ConvertFromInternalUnits(locationCurve.Curve.Length, UnitTypeId.Meters);

        // Pilares verticales sin curva de ubicación: altura de su caja envolvente
        var bb = element.get_BoundingBox(null);
        return bb != null
            ? UnitUtils.ConvertFromInternalUnits(Math.Abs(bb.Max.Z - bb.Min.Z), UnitTypeId.Meters)
            : 0;
    }
}
