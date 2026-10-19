using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CQEING_CuadroCantidades.Core.Application.Services;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Caras exteriores de columnas y vigas según las líneas de propiedad del modelo, que marcan el contorno del
/// edificio. Una cara lateral es exterior si un punto 30 cm hacia afuera de su centro queda fuera del contorno, así
/// que no depende de si se llama X1 o X2. Si el centro de alguna cara ya está fuera, la línea atraviesa el elemento
/// (o el elemento está fuera del lote) y no se clasifica.
/// </summary>
public sealed class BoundaryFaceClassifier
{
    // Distancia hacia afuera de la cara a la que se mira si se sale del contorno
    private static readonly double ProbeDistance = UnitUtils.ConvertToInternalUnits(30, UnitTypeId.Centimeters);

    // Distancia hacia adentro del centro de la cara para saber si el propio elemento está dentro del contorno
    private static readonly double InsetDistance = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Centimeters);

    // Tolerancia para unir los extremos de los lados de una línea de propiedad (≈ 1 mm)
    private const double JoinTolerance = 0.003;

    private readonly LotContour _contour;

    private BoundaryFaceClassifier(LotContour contour) => _contour = contour;

    /// <summary>
    /// Lee las líneas de propiedad cerradas del documento. Devuelve null si no hay ninguna.
    /// </summary>
    public static BoundaryFaceClassifier? Create(Document document)
    {
        var polygons = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_SiteProperty)
            .WhereElementIsNotElementType()
            .Select(ReadPolygon)
            .OfType<IReadOnlyList<(double X, double Y)>>()
            .ToList();

        var contour = new LotContour(polygons);
        return contour.IsEmpty ? null : new BoundaryFaceClassifier(contour);
    }

    /// <summary>
    /// Caras exteriores de una columna o viga, o que la línea la atraviesa. Null si el elemento no es columna ni viga
    /// o su geometría no se puede leer.
    /// </summary>
    public BoundaryClassification? Classify(Element element)
    {
        if (FaceAreaExtractor.GetKind(element) is not (FaceMeasuredKind.Column or FaceMeasuredKind.Beam))
            return null;

        var exterior = new HashSet<ElementFace>();
        try
        {
            foreach (var (face, transform, globalFace) in FaceAreaExtractor.EnumeratePlanarFaces(element))
            {
                if (globalFace is ElementFace.Z1 or ElementFace.Z2)
                    continue;

                XYZ normal = transform.OfVector(face.FaceNormal);
                var horizontal = new XYZ(normal.X, normal.Y, 0);
                if (horizontal.GetLength() < 1e-9)
                    continue;

                XYZ outward = horizontal.Normalize();
                BoundingBoxUV box = face.GetBoundingBox();
                XYZ center = transform.OfPoint(face.Evaluate((box.Min + box.Max) / 2.0));

                XYZ inner = center - outward * InsetDistance;
                if (!_contour.Contains(inner.X, inner.Y))
                    return new BoundaryClassification(new HashSet<ElementFace>(), CrossesBoundary: true);

                XYZ probe = center + outward * ProbeDistance;
                if (!_contour.Contains(probe.X, probe.Y))
                    exterior.Add(globalFace);
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Geometría ilegible: el elemento queda sin clasificar
            return null;
        }

        return new BoundaryClassification(exterior, CrossesBoundary: false);
    }

    /// <summary>
    /// Polígono en planta de una línea de propiedad, o null si está abierta o no tiene lados.
    /// Los lados curvos se reparten en puntos.
    /// </summary>
    private static IReadOnlyList<(double X, double Y)>? ReadPolygon(Element propertyLine)
    {
        GeometryElement? geometry = propertyLine.get_Geometry(new Options());
        if (geometry == null)
            return null;

        var pending = geometry.OfType<Curve>().ToList();
        if (pending.Count < 2)
            return null;

        // Los lados vienen en el orden del boceto; se encadenan por sus extremos por si alguno viene invertido
        var points = new List<XYZ>();
        Curve first = pending[0];
        pending.RemoveAt(0);
        XYZ start = first.GetEndPoint(0);
        IList<XYZ> firstPoints = first.Tessellate();
        AppendWithoutLast(firstPoints, points);
        XYZ end = firstPoints[^1];

        while (pending.Count > 0)
        {
            int index = pending.FindIndex(curve => Near(curve.GetEndPoint(0), end) || Near(curve.GetEndPoint(1), end));
            if (index < 0)
                return null;

            Curve next = pending[index];
            pending.RemoveAt(index);
            var nextPoints = next.Tessellate().ToList();
            if (!Near(next.GetEndPoint(0), end))
                nextPoints.Reverse();

            AppendWithoutLast(nextPoints, points);
            end = nextPoints[^1];
        }

        return Near(end, start) ? points.Select(p => (p.X, p.Y)).ToList() : null;
    }

    // El último punto de un lado es el primero del siguiente
    private static void AppendWithoutLast(IList<XYZ> source, List<XYZ> target)
    {
        for (int i = 0; i < source.Count - 1; i++)
        {
            target.Add(source[i]);
        }
    }

    private static bool Near(XYZ a, XYZ b) => Math.Abs(a.X - b.X) < JoinTolerance && Math.Abs(a.Y - b.Y) < JoinTolerance;
}

/// <summary>
/// Caras exteriores de un elemento según la línea de propiedad, o que la línea lo atraviesa.
/// </summary>
/// <param name="ExteriorFaces">Caras laterales que dan hacia afuera del contorno (vacío si atraviesa).</param>
/// <param name="CrossesBoundary">La línea pasa por el elemento o el elemento está fuera del lote.</param>
public sealed record BoundaryClassification(IReadOnlySet<ElementFace> ExteriorFaces, bool CrossesBoundary);
