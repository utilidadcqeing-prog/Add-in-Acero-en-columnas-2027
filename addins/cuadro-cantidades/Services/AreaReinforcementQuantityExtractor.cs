using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Extrae el área (m²) de un refuerzo de área estructural (parrillas o mallas de losas) para actividades en M2.
/// Es el área en planta de la losa hasta su cara exterior, sin descontar columnas y sin los huecos libres (escaleras,
/// ductos) ni las aberturas: lo que hay dentro del contorno más las vigas y columnas que lo tocan, completas. Así la
/// cantidad no depende de que el contorno, que en Revit se dibuja para generar las varillas, llegue al centímetro a
/// la cara exterior de vigas y columnas o al borde de los huecos. Para actividades en KG el refuerzo lleva además el
/// peso de las varillas que genera (<see cref="RebarInSystem"/>), así basta asignar el refuerzo; esas varillas no
/// llevan área, y el M2 se cuenta una sola vez aunque el refuerzo tenga varias capas.
/// </summary>
public static class AreaReinforcementQuantityExtractor
{
    // Tolerancia para unir extremos de bordes consecutivos (pies)
    private const double JoinTolerance = 1e-4;

    // Altura (pies) de los prismas con que se recortan los contornos en planta: área = volumen / altura
    private const double PrismHeight = 1.0;

    // Lado mínimo (pies, ≈ 1.8 mm) de los polígonos: Revit no crea líneas más cortas que ≈ 0.8 mm
    private const double MinSegment = 0.006;

    // Distancia (pies, ≈ 10 cm) a la que una viga o columna se considera pegada al contorno del refuerzo
    private const double TouchTolerance = 0.33;

    public static bool IsAreaReinforcement(Element element) =>
        element.Category?.Id.Value == (long)BuiltInCategory.OST_AreaRein;

    /// <summary>
    /// Agrega el área (m²) y el peso (kg) de sus varillas al diccionario si el elemento es un refuerzo de área.
    /// </summary>
    public static void AddQuantities(Element element, IDictionary<string, double> paramDict)
    {
        if (element is not AreaReinforcement areaReinforcement)
            return;

        var curves = areaReinforcement.GetBoundaryCurveIds()
            .Select(id => (element.Document.GetElement(id) as AreaReinforcementCurve)?.Curve)
            .Where(curve => curve != null)
            .Cast<Curve>()
            .ToList();

        var contour = ChainBoundary(curves);
        double areaFt2 = GetAreaInsideHost(areaReinforcement, contour) ?? GetPolygonArea(contour);
        if (areaFt2 > 0)
            paramDict["Area"] = UnitUtils.ConvertFromInternalUnits(areaFt2, UnitTypeId.SquareMeters);

        double weight = GetBarsWeight(areaReinforcement);
        if (weight > 0)
            paramDict["Peso"] = weight;
    }

    /// <summary>
    /// Peso (kg) de las varillas que genera el refuerzo, de todas sus capas, calculado igual que el de una barra suelta.
    /// </summary>
    private static double GetBarsWeight(AreaReinforcement areaReinforcement)
    {
        double weight = 0;
        foreach (var id in areaReinforcement.GetRebarInSystemIds())
        {
            if (areaReinforcement.Document.GetElement(id) is not { } bar)
                continue;

            var quantities = new Dictionary<string, double>();
            RebarQuantityExtractor.AddQuantities(bar, quantities);
            if (quantities.TryGetValue("Peso", out double barWeight))
                weight += barWeight;
        }

        return weight;
    }

    /// <summary>
    /// Escribe en el JSON de un refuerzo de área ya asignado su área ("Area" y "Área") y su peso ("Peso") actuales, con
    /// 2 decimales como en la extracción, para que los cambios del contorno, de los huecos de la losa o de las
    /// varillas lleguen a la actividad sin reasignarlo.
    /// </summary>
    /// <param name="element">Elemento de Revit.</param>
    /// <param name="json">JSON guardado del elemento.</param>
    /// <param name="updatedJson">JSON con el área y el peso actuales, o el mismo si no cambió.</param>
    /// <returns>True si el JSON cambió.</returns>
    public static bool TrySyncElementData(Element element, string? json, out string updatedJson)
    {
        updatedJson = json ?? "{}";

        var quantities = new Dictionary<string, double>();
        AddQuantities(element, quantities);
        if (quantities.Count == 0)
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

        var values = new List<(string Key, double Value)>();
        if (quantities.TryGetValue("Area", out double area))
        {
            values.Add(("Area", area));
            values.Add(("Área", area));
        }

        if (quantities.TryGetValue("Peso", out double weight))
            values.Add(("Peso", weight));

        bool changed = false;
        foreach (var (key, value) in values)
        {
            double rounded = Math.Round(value, 2);
            bool same = data.TryGetValue(key, out var existing)
                && existing is JsonElement { ValueKind: JsonValueKind.Number } number
                && number.TryGetDouble(out double stored)
                && Math.Abs(stored - rounded) < 0.005;
            if (!same)
            {
                data[key] = rounded;
                changed = true;
            }
        }

        if (changed)
            updatedJson = JsonSerializer.Serialize(data, CQEING_CuadroCantidades.Core.JsonConfig.RelaxedOptions);

        return changed;
    }

    /// <summary>
    /// Área (pies²) del contorno dentro de la losa anfitriona (su boceto más las vigas y columnas que llegan a ella),
    /// más las vigas que tocan el contorno y las columnas que tocan el contorno o esas vigas, completas, y fuera de
    /// las aberturas de la losa; o null si el anfitrión no es una losa o la geometría no se pudo recortar (se usa
    /// entonces el contorno completo).
    /// </summary>
    private static double? GetAreaInsideHost(AreaReinforcement areaReinforcement, IReadOnlyList<XYZ> contour)
    {
        Document doc = areaReinforcement.Document;
        if (doc.GetElement(areaReinforcement.GetHostId()) is not Floor floor || contour.Count < 3)
            return null;

        try
        {
            Solid? area = CreatePrism(contour);
            if (area == null)
                return null;

            Solid contourPrism = area;
            var beams = GetPlanFootprints(floor, BuiltInCategory.OST_StructuralFraming);
            var columns = GetPlanFootprints(floor, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns);

            // La losa suele dibujarse entre las vigas o alrededor de las columnas: lo que ocupan también es losa,
            // así que fuera del boceto solo quedan los huecos libres
            Solid? footprint = GetFootprint(floor);
            if (footprint != null)
            {
                foreach (var (prism, _) in beams.Concat(columns))
                {
                    footprint = TryUnion(footprint, prism);
                }

                area = BooleanOperationsUtils.ExecuteBooleanOperation(area, footprint, BooleanOperationsType.Intersect);
            }

            // Vigas que tocan el contorno: completas, hasta su cara exterior, aunque el contorno quede corto en el
            // borde de la losa o rodee un hueco por fuera de ellas
            var touchingBeams = beams.Where(beam => Touches(beam, contour, contourPrism)).ToList();
            foreach (var (prism, _) in touchingBeams)
            {
                area = TryUnion(area, prism);
            }

            // Columnas que tocan el contorno o esas vigas (las de las esquinas): completas
            foreach (var (prism, outline) in columns)
            {
                if (Touches((prism, outline), contour, contourPrism)
                    || touchingBeams.Any(beam => IsNear(outline, beam.Outline)))
                    area = TryUnion(area, prism);
            }

            foreach (var opening in GetOpenings(floor))
            {
                if (CreatePrism(opening) is { } openingPrism)
                    area = BooleanOperationsUtils.ExecuteBooleanOperation(area, openingPrism, BooleanOperationsType.Difference);
            }

            return area.Volume / PrismHeight;
        }
        catch
        {
            // Boceto o aberturas ilegibles: el área queda la del contorno completo
            return null;
        }
    }

    /// <summary>
    /// Planta de la losa según su boceto: sus contornos exteriores sin los huecos dibujados en él. Un lazo es hueco
    /// si queda dentro de un número impar de lazos (una isla dentro de un hueco vuelve a sumar).
    /// </summary>
    private static Solid? GetFootprint(Floor floor)
    {
        if (floor.Document.GetElement(floor.SketchId) is not Sketch sketch)
            return null;

        var loops = new List<List<XYZ>>();
        foreach (CurveArray loop in sketch.Profile)
        {
            var points = ChainBoundary(loop.Cast<Curve>().ToList());
            if (points.Count >= 3)
                loops.Add(points);
        }

        Solid? footprint = null;
        foreach (var (loop, depth) in loops
            .Select(loop => (loop, depth: loops.Count(other => other != loop && Contains(other, loop[0]))))
            .OrderBy(item => item.depth))
        {
            if (CreatePrism(loop) is not { } prism)
                continue;

            if (depth % 2 == 0)
                footprint = footprint == null
                    ? prism
                    : BooleanOperationsUtils.ExecuteBooleanOperation(footprint, prism, BooleanOperationsType.Union);
            else if (footprint != null)
                footprint = BooleanOperationsUtils.ExecuteBooleanOperation(footprint, prism, BooleanOperationsType.Difference);
        }

        return footprint;
    }

    /// <summary>
    /// Contornos en planta de las aberturas de la losa: las alojadas en ella (por cara o verticales) y las aberturas
    /// de agujero que la atraviesan.
    /// </summary>
    private static IEnumerable<List<XYZ>> GetOpenings(Floor floor)
    {
        BoundingBoxXYZ? floorBox = floor.get_BoundingBox(null);

        foreach (Opening opening in new FilteredElementCollector(floor.Document).OfClass(typeof(Opening)))
        {
            bool cutsFloor = opening.Host?.Id == floor.Id
                || (opening.Host == null
                    && opening.Category?.Id.Value == (long)BuiltInCategory.OST_ShaftOpening
                    && OverlapsVertically(opening.get_BoundingBox(null), floorBox));
            if (!cutsFloor)
                continue;

            if (opening.IsRectBoundary)
            {
                var corners = opening.BoundaryRect;
                if (corners.Count == 2)
                {
                    XYZ a = corners[0], b = corners[1];
                    yield return new List<XYZ>
                    {
                        new(a.X, a.Y, 0), new(b.X, a.Y, 0), new(b.X, b.Y, 0), new(a.X, b.Y, 0),
                    };
                }
            }
            else
            {
                var points = ChainBoundary(opening.BoundaryCurves.Cast<Curve>().ToList());
                if (points.Count >= 3)
                    yield return points;
            }
        }
    }

    /// <summary>
    /// Indica si la planta de un elemento queda dentro del contorno, lo cruza o está a menos de
    /// <see cref="TouchTolerance"/> de él.
    /// </summary>
    private static bool Touches((Solid Prism, List<XYZ> Outline) element, IReadOnlyList<XYZ> contour, Solid contourPrism)
    {
        if (IsNear(element.Outline, contour))
            return true;

        try
        {
            var overlap = BooleanOperationsUtils.ExecuteBooleanOperation(element.Prism, contourPrism, BooleanOperationsType.Intersect);
            return overlap.Volume > 1e-6;
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Indica si algún vértice del primer polígono queda dentro del segundo o a menos de <see cref="TouchTolerance"/>
    /// de sus bordes.
    /// </summary>
    private static bool IsNear(IReadOnlyList<XYZ> outline, IReadOnlyList<XYZ> polygon) =>
        outline.Any(point => Contains(polygon, point) || DistanceToEdges(polygon, point) <= TouchTolerance);

    /// <summary>
    /// Une dos sólidos; si Revit no logra unirlos, deja el primero (se pierde solo esa pieza, no todo el cálculo).
    /// </summary>
    private static Solid TryUnion(Solid solid, Solid other)
    {
        try
        {
            return BooleanOperationsUtils.ExecuteBooleanOperation(solid, other, BooleanOperationsType.Union);
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            return solid;
        }
    }

    /// <summary>
    /// Planta (prisma y vértices) de los elementos de las categorías dadas que llegan a la losa: su proyección
    /// vertical, que sigue la sección real aunque estén girados o sean circulares.
    /// </summary>
    private static List<(Solid Prism, List<XYZ> Outline)> GetPlanFootprints(Floor floor, params BuiltInCategory[] categories)
    {
        var footprints = new List<(Solid, List<XYZ>)>();
        BoundingBoxXYZ? box = floor.get_BoundingBox(null);
        if (box == null)
            return footprints;

        var elements = new FilteredElementCollector(floor.Document)
            .WherePasses(new ElementMulticategoryFilter(categories))
            .WherePasses(new BoundingBoxIntersectsFilter(new Outline(box.Min, box.Max)))
            .WhereElementIsNotElementType();

        var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
        foreach (Element element in elements)
        {
            foreach (var solid in GetSolids(element.get_Geometry(new Options())))
            {
                try
                {
                    Face face = ExtrusionAnalyzer.Create(solid, plane, XYZ.BasisZ).GetExtrusionBase();
                    foreach (CurveLoop loop in face.GetEdgesAsCurveLoops())
                    {
                        var outline = loop.SelectMany(curve =>
                        {
                            var points = curve.Tessellate();
                            return points.Take(points.Count - 1);
                        }).ToList();
                        if (CreatePrism(outline) is { } prism)
                            footprints.Add((prism, outline));
                    }
                }
                catch
                {
                    // Sólido sin proyección legible: esa parte del elemento no se suma
                }
            }
        }

        return footprints;
    }

    private static IEnumerable<Solid> GetSolids(GeometryElement? geometry)
    {
        if (geometry == null)
            yield break;

        foreach (GeometryObject obj in geometry)
        {
            if (obj is Solid solid && solid.Volume > 0)
            {
                yield return solid;
            }
            else if (obj is GeometryInstance instance)
            {
                foreach (var nested in GetSolids(instance.GetInstanceGeometry()))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>
    /// Distancia en planta del punto al borde más cercano del polígono.
    /// </summary>
    private static double DistanceToEdges(IReadOnlyList<XYZ> polygon, XYZ point)
    {
        double min = double.MaxValue;
        for (int i = 0; i < polygon.Count; i++)
        {
            XYZ a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double lengthSquared = dx * dx + dy * dy;
            double t = lengthSquared > 0
                ? Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0, 1)
                : 0;
            double ex = a.X + t * dx - point.X, ey = a.Y + t * dy - point.Y;
            min = Math.Min(min, Math.Sqrt(ex * ex + ey * ey));
        }

        return min;
    }

    private static bool OverlapsVertically(BoundingBoxXYZ? a, BoundingBoxXYZ? b) =>
        a != null && b != null && a.Min.Z <= b.Max.Z && b.Min.Z <= a.Max.Z;

    /// <summary>
    /// Prisma de altura <see cref="PrismHeight"/> sobre el polígono llevado a Z = 0, o null si el polígono es degenerado.
    /// </summary>
    private static Solid? CreatePrism(IReadOnlyList<XYZ> polygon)
    {
        var points = new List<XYZ>();
        foreach (var p in polygon)
        {
            var flat = new XYZ(p.X, p.Y, 0);
            if (points.Count == 0 || flat.DistanceTo(points[^1]) > MinSegment)
                points.Add(flat);
        }

        while (points.Count > 1 && points[0].DistanceTo(points[^1]) <= MinSegment)
            points.RemoveAt(points.Count - 1);

        if (points.Count < 3)
            return null;

        // Sentido antihorario, el de un contorno exterior
        if (GetSignedArea(points) < 0)
            points.Reverse();

        var loop = new CurveLoop();
        for (int i = 0; i < points.Count; i++)
        {
            loop.Append(Line.CreateBound(points[i], points[(i + 1) % points.Count]));
        }

        return GeometryCreationUtilities.CreateExtrusionGeometry(new[] { loop }, XYZ.BasisZ, PrismHeight);
    }

    /// <summary>
    /// Indica si el punto queda dentro del polígono en planta (cruce de rayo).
    /// </summary>
    private static bool Contains(IReadOnlyList<XYZ> polygon, XYZ point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            XYZ a = polygon[i], b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }

    /// <summary>
    /// Ordena los bordes en un recorrido continuo (Revit no garantiza el orden) y devuelve sus puntos
    /// teselados; los bordes curvos quedan aproximados por segmentos.
    /// </summary>
    private static List<XYZ> ChainBoundary(List<Curve> curves)
    {
        var points = new List<XYZ>();
        if (curves.Count == 0)
            return points;

        var pending = new List<Curve>(curves);
        var first = pending[0].Tessellate().ToList();
        pending.RemoveAt(0);
        points.AddRange(first.Take(first.Count - 1));
        XYZ end = first[^1];

        while (pending.Count > 0)
        {
            int index = pending.FindIndex(c =>
                c.GetEndPoint(0).IsAlmostEqualTo(end, JoinTolerance) || c.GetEndPoint(1).IsAlmostEqualTo(end, JoinTolerance));
            if (index < 0)
                break; // Contorno abierto o con varios lazos: se calcula con lo que esté encadenado

            var next = pending[index];
            pending.RemoveAt(index);
            var tessellated = next.Tessellate().ToList();
            if (!next.GetEndPoint(0).IsAlmostEqualTo(end, JoinTolerance))
                tessellated.Reverse();

            points.AddRange(tessellated.Take(tessellated.Count - 1));
            end = tessellated[^1];
        }

        return points;
    }

    /// <summary>
    /// Área en planta del polígono (fórmula del área de Gauss sobre X e Y).
    /// </summary>
    private static double GetPolygonArea(IReadOnlyList<XYZ> points) =>
        points.Count < 3 ? 0 : Math.Abs(GetSignedArea(points));

    /// <summary>
    /// Área con signo del polígono en planta: positiva si se recorre en sentido antihorario.
    /// </summary>
    private static double GetSignedArea(IReadOnlyList<XYZ> points)
    {
        double doubleArea = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var q = points[(i + 1) % points.Count];
            doubleArea += p.X * q.Y - q.X * p.Y;
        }

        return doubleArea / 2.0;
    }
}
