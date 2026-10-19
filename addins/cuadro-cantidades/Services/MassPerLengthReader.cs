using Autodesk.Revit.DB;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Lee parámetros de masa por unidad de longitud (kg/m) buscándolos por su tipo de dato oficial
/// (<see cref="SpecTypeId.MassPerUnitLength"/>), de forma independiente del idioma y del nombre del parámetro.
/// </summary>
public static class MassPerLengthReader
{
    /// <summary>
    /// Devuelve el primer valor positivo, en kg/m, de un parámetro de masa por unidad de longitud del elemento,
    /// o 0 si no tiene ninguno con valor.
    /// </summary>
    public static double FindKilogramsPerMeter(Element source)
    {
        foreach (Parameter p in source.Parameters)
        {
            if (!p.HasValue || p.StorageType != StorageType.Double)
                continue;

            if (p.Definition.GetDataType() != SpecTypeId.MassPerUnitLength)
                continue;

            // Revit guarda este dato en unidades internas (kg/pie): convertir siempre a kg/m
            double kgm = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.KilogramsPerMeter);
            if (kgm > 0)
                return kgm;
        }

        return 0;
    }
}
