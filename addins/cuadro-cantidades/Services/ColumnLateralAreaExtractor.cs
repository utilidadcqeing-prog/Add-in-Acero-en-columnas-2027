using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using CQEING_CuadroCantidades.Core.Application.Services;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Calcula el área lateral útil de pilares (estructurales y arquitectónicos) para actividades en M2
/// como pañete, pintura o estuco. Suma las caras verticales del sólido real del elemento, así que
/// sirve para cualquier sección (rectangular, circular, en L) y, si el pilar está unido a vigas o
/// losas, Revit ya recorta el sólido y la altura sale neta. A las caras planas se les resta lo que
/// tapan los elementos pegados; de los muros, solo su núcleo.
/// </summary>
public static class ColumnLateralAreaExtractor
{
    // Una cara es vertical si su normal casi no tiene componente Z (≈ 0,6° de tolerancia)
    private const double VerticalNormalTolerance = 0.01;

    /// <summary>
    /// Indica si el elemento es un pilar estructural o arquitectónico.
    /// </summary>
    public static bool IsColumnCategory(Element element) =>
        element.Category?.Id.Value
            is (long)BuiltInCategory.OST_StructuralColumns
                or (long)BuiltInCategory.OST_Columns;

    /// <summary>
    /// Agrega el área lateral (m²) al diccionario si el elemento es un pilar con geometría sólida.
    /// </summary>
    public static void AddQuantities(Element element, IDictionary<string, double> paramDict)
    {
        if (!IsColumnCategory(element))
            return;

        double areaFt2;
        try
        {
            var options = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
            areaFt2 = SumVerticalFaceArea(element.get_Geometry(options), OccupiedFaceAreaCalculator.Create(element));
        }
        catch
        {
            // Geometría ilegible: el pilar queda sin área lateral en lugar de interrumpir la extracción
            return;
        }

        if (areaFt2 > 0)
            paramDict[QuantificationEngine.LateralAreaKey] =UnitUtils.ConvertFromInternalUnits(areaFt2, UnitTypeId.SquareMeters);
    }

    private static double SumVerticalFaceArea(GeometryElement? geometry, OccupiedFaceAreaCalculator occupied)
    {
        if (geometry == null)
            return 0;

        double total = 0;
        foreach (GeometryObject obj in geometry)
        {
            switch (obj)
            {
                case Solid solid when solid.Volume > 0:
                    foreach (Face face in solid.Faces)
                    {
                        if (!IsVertical(face))
                            continue;

                        // La geometría de instancia ya viene en coordenadas del proyecto. Las caras curvas no se descuentan.
                        total += face is PlanarFace planar
                            ? Math.Max(0, face.Area - occupied.OccupiedArea(planar, Transform.Identity))
                            : face.Area;
                    }
                    break;

                // Pilares sin cortes ni uniones traen su geometría dentro de la instancia de familia
                case GeometryInstance instance:
                    total += SumVerticalFaceArea(instance.GetInstanceGeometry(), occupied);
                    break;
            }
        }

        return total;
    }

    private static bool IsVertical(Face face)
    {
        // Normal en el centro paramétrico de la cara: cubre caras planas y cilíndricas de pilares circulares
        BoundingBoxUV box = face.GetBoundingBox();
        UV center = (box.Min + box.Max) / 2.0;
        XYZ normal = face.ComputeNormal(center);
        return Math.Abs(normal.Z) < VerticalNormalTolerance;
    }
}
