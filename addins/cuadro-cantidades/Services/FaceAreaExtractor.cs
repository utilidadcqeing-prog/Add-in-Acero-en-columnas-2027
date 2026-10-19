using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using CQEING_CuadroCantidades.Core.Application.Services;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Calcula el área útil (m²) de cada cara de columnas, vigas y zapatas según los ejes globales del proyecto, para que
/// en actividades en M2 se midan solo las caras elegidas. En columnas y vigas recorre el sólido real del elemento, así
/// que los recortes de sus uniones ya vienen descontados, y además resta lo que tapan los elementos pegados (de los
/// muros, solo su núcleo). En zapatas recorre la geometría original de la familia: la cara superior cuenta también el
/// área donde nace la columna y el hueco de una columna embebida no suma a los costados. Las caras curvas se reparten
/// por sus triángulos y no se les descuenta lo que tapan sus vecinos.
/// En columnas y vigas también guarda cuáles caras laterales dan hacia afuera de la línea de propiedad del modelo.
/// </summary>
public static class FaceAreaExtractor
{
    // Una cara de viga es cabeza si su normal va casi en el sentido del eje de la viga (más de 45°)
    private const double BeamEndAlignment = 0.7;

    // Detalle de la malla con la que se reparten las caras curvas (0 = gruesa, 1 = fina)
    private const double CurvedFaceDetail = 0.5;

    /// <summary>
    /// Indica si el elemento es columna (estructural o arquitectónica), viga o zapata.
    /// </summary>
    public static FaceMeasuredKind GetKind(Element element) => element.Category?.Id.Value switch
    {
        (long)BuiltInCategory.OST_StructuralColumns or (long)BuiltInCategory.OST_Columns => FaceMeasuredKind.Column,
        (long)BuiltInCategory.OST_StructuralFraming => FaceMeasuredKind.Beam,
        (long)BuiltInCategory.OST_StructuralFoundation => FaceMeasuredKind.Footing,
        _ => FaceMeasuredKind.None,
    };

    /// <summary>
    /// Agrega al diccionario el área (m²) de cada cara si el elemento es columna, viga o zapata con geometría sólida.
    /// </summary>
    public static void AddQuantities(Element element, IDictionary<string, double> paramDict)
    {
        foreach (var (face, area) in ComputeFaceAreas(element))
        {
            paramDict[FaceAreaCalculator.AreaKey(face)] = Math.Round(area, FaceAreaCalculator.StoredDecimals);
        }
    }

    /// <summary>
    /// Caras que se miden por defecto en una columna, viga o zapata recién extraída y, en columnas y vigas, sus caras
    /// exteriores según la línea de propiedad, como texto para el JSON.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetData(Element element)
    {
        var kind = GetKind(element);
        if (kind == FaceMeasuredKind.None)
            return new Dictionary<string, string>();

        var data = new Dictionary<string, string>
        {
            [FaceAreaCalculator.SelectionKey] = FaceAreaCalculator.Format(FaceAreaCalculator.DefaultSelection(kind)),
        };
        AddBoundaryData(element, data);
        return data;
    }

    /// <summary>
    /// Escribe en el JSON de un elemento ya asignado sus áreas por cara actuales y, si no tiene caras elegidas,
    /// las de por defecto. Conserva las caras que el usuario ya eligió.
    /// </summary>
    /// <param name="element">Elemento de Revit.</param>
    /// <param name="json">JSON guardado del elemento.</param>
    /// <param name="updatedJson">JSON con los datos de caras, o el mismo si no cambió nada.</param>
    /// <returns>True si el JSON cambió.</returns>
    public static bool TrySyncElementData(Element element, string? json, out string updatedJson)
    {
        updatedJson = json ?? "{}";
        var kind = GetKind(element);
        if (kind == FaceMeasuredKind.None)
            return false;

        var areas = ComputeFaceAreas(element);
        if (areas.Count == 0)
            return false;

        Dictionary<string, object> data;
        try
        {
            data = string.IsNullOrWhiteSpace(json)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
        }
        catch (JsonException)
        {
            data = new Dictionary<string, object>();
        }

        bool changed = false;
        foreach (var face in FaceAreaCalculator.BeamFaces)
        {
            string key = FaceAreaCalculator.AreaKey(face);
            if (areas.TryGetValue(face, out double area))
            {
                double rounded = Math.Round(area, FaceAreaCalculator.StoredDecimals);
                if (!data.TryGetValue(key, out var existing) || !IsSameNumber(existing, rounded))
                {
                    data[key] = rounded;
                    changed = true;
                }
            }
            else if (data.Remove(key))
            {
                changed = true;
            }
        }

        // Las caras elegidas pueden venir de la API como "CARAS": se conservan
        bool hasSelection = data.Keys.Any(k => string.Equals(k, FaceAreaCalculator.SelectionKey, StringComparison.OrdinalIgnoreCase));
        if (!hasSelection)
        {
            data[FaceAreaCalculator.SelectionKey] = FaceAreaCalculator.Format(FaceAreaCalculator.DefaultSelection(kind));
            changed = true;
        }

        // Caras exteriores según la línea de propiedad: se recalculan siempre y se borran si ya no aplican
        var boundary = new Dictionary<string, string>();
        AddBoundaryData(element, boundary);
        foreach (var key in new[] { FaceAreaCalculator.ExteriorFacesKey, FaceAreaCalculator.BoundaryIssueKey })
        {
            if (boundary.TryGetValue(key, out string? value))
            {
                if (!data.TryGetValue(key, out var existing) || AsString(existing) != value)
                {
                    data[key] = value;
                    changed = true;
                }
            }
            else if (data.Remove(key))
            {
                changed = true;
            }
        }

        if (changed)
            updatedJson = JsonSerializer.Serialize(data, CQEING_CuadroCantidades.Core.JsonConfig.RelaxedOptions);

        return changed;
    }

    /// <summary>
    /// Área útil (m²) de cada cara del elemento. En columnas solo las laterales; en vigas, sin las cabezas. A columnas
    /// y vigas se les resta lo que tapan los elementos pegados (de los muros, solo su núcleo). En zapatas, las seis
    /// completas, sin descontar la columna.
    /// </summary>
    public static IReadOnlyDictionary<ElementFace, double> ComputeFaceAreas(Element element)
    {
        var areasFt2 = new Dictionary<ElementFace, double>();
        try
        {
            var occupied = GetKind(element) is FaceMeasuredKind.Column or FaceMeasuredKind.Beam
                ? OccupiedFaceAreaCalculator.Create(element)
                : null;

            foreach (var (solidFace, transform, faceAreas) in ClassifyFaces(element, computeReferences: false, original: true))
            {
                // Una cara plana va entera a una sola cara global; las curvas se reparten y no se descuentan
                if (occupied != null && solidFace is PlanarFace planar && faceAreas.Count == 1)
                {
                    var (globalFace, area) = faceAreas.Single();
                    faceAreas[globalFace] = Math.Max(0, area - occupied.OccupiedArea(planar, transform));
                }

                foreach (var (face, area) in faceAreas)
                {
                    areasFt2[face] = areasFt2.TryGetValue(face, out double sum) ? sum + area : area;
                }
            }
        }
        catch
        {
            // Geometría ilegible: el elemento queda sin áreas por cara en lugar de interrumpir la extracción
            return new Dictionary<ElementFace, double>();
        }

        return areasFt2
            .Where(kvp => kvp.Value > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => UnitUtils.ConvertFromInternalUnits(kvp.Value, UnitTypeId.SquareMeters)
            );
    }

    /// <summary>
    /// Referencias de las caras del sólido que pertenecen a las caras elegidas, para seleccionarlas en Revit.
    /// Una cara curva se toma si la mayor parte de su área va a una cara elegida. Usa el sólido real, porque las
    /// caras de la geometría original no se pueden seleccionar.
    /// </summary>
    public static IReadOnlyList<Reference> GetFaceReferences(Element element, IReadOnlySet<ElementFace> selection)
    {
        var references = new List<Reference>();
        if (selection.Count == 0)
            return references;

        try
        {
            foreach (var (face, _, faceAreas) in ClassifyFaces(element, computeReferences: true, original: false))
            {
                if (face.Reference == null || faceAreas.Count == 0)
                    continue;

                var dominant = faceAreas.MaxBy(kvp => kvp.Value).Key;
                if (selection.Contains(dominant))
                    references.Add(face.Reference);
            }
        }
        catch
        {
            // Geometría ilegible: no se resalta ninguna cara del elemento
            return Array.Empty<Reference>();
        }

        return references;
    }

    /// <summary>
    /// Caras planas del sólido real de una columna, viga o zapata, con la cara global a la que pertenecen y la
    /// transformación a coordenadas del proyecto. No incluye las cabezas de las vigas.
    /// </summary>
    internal static IEnumerable<(PlanarFace Face, Transform Transform, ElementFace GlobalFace)> EnumeratePlanarFaces(Element element)
    {
        foreach (var (face, transform, areas) in ClassifyFaces(element, computeReferences: false, original: false))
        {
            if (face is PlanarFace planar && areas.Count == 1)
                yield return (planar, transform, areas.Keys.First());
        }
    }

    /// <summary>
    /// Agrega las caras exteriores según la línea de propiedad, o el aviso de que la línea atraviesa el elemento.
    /// No agrega nada si no es columna ni viga o si el modelo no tiene línea de propiedad.
    /// </summary>
    private static void AddBoundaryData(Element element, IDictionary<string, string> data)
    {
        var classification = BoundaryFaceClassifier.Create(element.Document)?.Classify(element);
        if (classification == null)
            return;

        if (classification.CrossesBoundary)
            data[FaceAreaCalculator.BoundaryIssueKey] = FaceAreaCalculator.BoundaryCrossedValue;
        else
            data[FaceAreaCalculator.ExteriorFacesKey] = FaceAreaCalculator.Format(classification.ExteriorFaces);
    }

    private static string? AsString(object? value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
        string s => s,
        _ => null,
    };

    /// <summary>
    /// Cada cara del sólido del elemento con el área (pies²) que aporta a cada cara global.
    /// </summary>
    /// <param name="original">En zapatas, recorre la geometría de la familia sin uniones ni cortes.</param>
    private static IEnumerable<(Face Face, Transform Transform, Dictionary<ElementFace, double> Areas)> ClassifyFaces(
        Element element,
        bool computeReferences,
        bool original
    )
    {
        var kind = GetKind(element);
        if (kind == FaceMeasuredKind.None)
            yield break;

        XYZ? beamAxis = kind == FaceMeasuredKind.Beam ? GetBeamAxis(element) : null;
        var options = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = computeReferences };

        // La geometría original viene en coordenadas de la familia; las zapatas de muro o de losa no la tienen
        var (geometry, transform) = original && kind == FaceMeasuredKind.Footing && element is FamilyInstance instance
            ? (instance.GetOriginalGeometry(options), instance.GetTotalTransform())
            : (element.get_Geometry(options), Transform.Identity);

        foreach (var (face, faceTransform) in EnumerateFaces(geometry, transform))
        {
            yield return (face, faceTransform, ClassifyFace(face, faceTransform, kind, beamAxis));
        }
    }

    private static IEnumerable<(Face Face, Transform Transform)> EnumerateFaces(GeometryElement? geometry, Transform transform)
    {
        if (geometry == null)
            yield break;

        foreach (GeometryObject obj in geometry)
        {
            if (obj is Solid solid && solid.Volume > 0)
            {
                foreach (Face face in solid.Faces)
                {
                    yield return (face, transform);
                }
            }
            // Elementos sin cortes ni uniones traen su geometría en la instancia de familia. Se recorre la del símbolo,
            // cuyas caras tienen referencias que Revit puede seleccionar, llevada a coordenadas del proyecto.
            else if (obj is GeometryInstance instance)
            {
                foreach (var item in EnumerateFaces(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform)))
                {
                    yield return item;
                }
            }
        }
    }

    private static Dictionary<ElementFace, double> ClassifyFace(
        Face face,
        Transform transform,
        FaceMeasuredKind kind,
        XYZ? beamAxis
    )
    {
        var areas = new Dictionary<ElementFace, double>();
        if (face is PlanarFace planar)
        {
            Add(transform.OfVector(planar.FaceNormal), face.Area, kind, beamAxis, areas);
            return areas;
        }

        // Cara curva (columna circular, viga curva): cada triángulo suma a la cara de su normal
        Mesh mesh = face.Triangulate(CurvedFaceDetail);
        for (int i = 0; i < mesh.NumTriangles; i++)
        {
            MeshTriangle triangle = mesh.get_Triangle(i);
            XYZ a = triangle.get_Vertex(0);
            XYZ b = triangle.get_Vertex(1);
            XYZ c = triangle.get_Vertex(2);
            XYZ cross = (b - a).CrossProduct(c - a);
            double area = cross.GetLength() / 2.0;
            if (area <= 0)
                continue;

            Add(transform.OfVector(OutwardNormal(face, (a + b + c) / 3.0, cross)), area, kind, beamAxis, areas);
        }

        return areas;
    }

    private static void Add(
        XYZ normal,
        double areaFt2,
        FaceMeasuredKind kind,
        XYZ? beamAxis,
        Dictionary<ElementFace, double> areasFt2
    )
    {
        if (beamAxis != null && Math.Abs(normal.DotProduct(beamAxis)) > BeamEndAlignment)
            return;

        var face = FaceAreaCalculator.Classify(normal.X, normal.Y, normal.Z);
        if (kind == FaceMeasuredKind.Column && face is ElementFace.Z1 or ElementFace.Z2)
            return;

        areasFt2[face] = areasFt2.TryGetValue(face, out double sum) ? sum + areaFt2 : areaFt2;
    }

    /// <summary>
    /// Normal hacia afuera del sólido en el centro de un triángulo: la de la cara en ese punto,
    /// porque la malla no garantiza el sentido de sus triángulos.
    /// </summary>
    private static XYZ OutwardNormal(Face face, XYZ point, XYZ triangleCross)
    {
        IntersectionResult? projection = face.Project(point);
        return projection != null
            ? face.ComputeNormal(projection.UVPoint)
            : triangleCross.Normalize();
    }

    /// <summary>
    /// Dirección del eje de la viga, o null si no tiene eje (las cabezas no se pueden distinguir).
    /// </summary>
    private static XYZ? GetBeamAxis(Element element)
    {
        if (element.Location is not LocationCurve { Curve: { } curve })
            return null;

        XYZ direction = curve.GetEndPoint(1) - curve.GetEndPoint(0);
        return direction.GetLength() > 1e-9 ? direction.Normalize() : null;
    }

    // Mitad del último decimal guardado: las áreas viejas guardadas con 2 decimales se recalculan
    private const double SameNumberTolerance = 0.00005;

    private static bool IsSameNumber(object? existing, double value) => existing switch
    {
        JsonElement { ValueKind: JsonValueKind.Number } je => je.TryGetDouble(out double d) && Math.Abs(d - value) < SameNumberTolerance,
        double d => Math.Abs(d - value) < SameNumberTolerance,
        _ => false,
    };
}
