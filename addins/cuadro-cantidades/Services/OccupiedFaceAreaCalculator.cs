using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Área de las caras de una columna o viga que ocupan los elementos pegados a ella (muros, vigas, columnas, losas y
/// zapatas), para medir solo el área útil que recibe acabados. De los muros cuenta solo el núcleo: el pañete del muro
/// y el de la columna se encuentran en la esquina. Lo que Revit ya recortó del sólido por una unión no se vuelve a
/// descontar, porque esa parte de la cara ya no existe.
/// </summary>
public sealed class OccupiedFaceAreaCalculator
{
    // Espesor de la lámina que se extruye desde la cara para buscar contactos (5 mm)
    private static readonly double ContactDepth = UnitUtils.ConvertToInternalUnits(5, UnitTypeId.Millimeters);

    private static readonly ICollection<BuiltInCategory> NeighborCategories = new[]
    {
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_Columns,
        BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_StructuralFoundation,
    };

    private static readonly OccupiedFaceAreaCalculator Empty = new(Array.Empty<Solid>());

    private readonly IReadOnlyList<Solid> _neighborSolids;

    private OccupiedFaceAreaCalculator(IReadOnlyList<Solid> neighborSolids) => _neighborSolids = neighborSolids;

    /// <summary>
    /// Reúne los sólidos de los elementos que tocan al elemento. Si no hay vecinos o no se pueden leer,
    /// devuelve una calculadora que no descuenta nada.
    /// </summary>
    public static OccupiedFaceAreaCalculator Create(Element element)
    {
        BoundingBoxXYZ? box = element.get_BoundingBox(null);
        if (box == null)
            return Empty;

        var margin = new XYZ(ContactDepth * 2, ContactDepth * 2, ContactDepth * 2);
        var neighbors = new FilteredElementCollector(element.Document)
            .WherePasses(new ElementMulticategoryFilter(NeighborCategories))
            .WherePasses(new BoundingBoxIntersectsFilter(new Outline(box.Min - margin, box.Max + margin)))
            .WhereElementIsNotElementType()
            .Excluding(new[] { element.Id });

        var solids = neighbors.SelectMany(GetContactSolids).ToList();
        return solids.Count == 0 ? Empty : new OccupiedFaceAreaCalculator(solids);
    }

    /// <summary>
    /// Área (pies²) de la cara que tapan los vecinos: se extruye la cara 5 mm hacia afuera, se le restan los
    /// vecinos y el volumen perdido dividido por los 5 mm es el área tapada. Si dos vecinos se traslapan,
    /// la zona común se cuenta una sola vez.
    /// </summary>
    /// <param name="face">Cara plana del sólido del elemento.</param>
    /// <param name="transform">Transformación que lleva la cara a coordenadas del proyecto.</param>
    public double OccupiedArea(PlanarFace face, Transform transform)
    {
        if (_neighborSolids.Count == 0)
            return 0;

        Solid slab;
        try
        {
            var loops = face.GetEdgesAsCurveLoops()
                .Select(loop => CurveLoop.CreateViaTransform(loop, transform))
                .ToList();
            XYZ outward = transform.OfVector(face.FaceNormal).Normalize();
            slab = GeometryCreationUtilities.CreateExtrusionGeometry(loops, outward, ContactDepth);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Bordes que Revit no puede extruir: la cara queda completa
            return 0;
        }

        double slabVolume = slab.Volume;
        Solid? free = slab;
        foreach (Solid neighbor in _neighborSolids)
        {
            try
            {
                free = BooleanOperationsUtils.ExecuteBooleanOperation(free, neighbor, BooleanOperationsType.Difference);
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Revit no pudo restar este vecino (geometría muy justa): no se descuenta
                continue;
            }

            if (free == null || free.Volume <= 0)
            {
                free = null;
                break;
            }
        }

        double occupied = (slabVolume - (free?.Volume ?? 0)) / ContactDepth;
        return Math.Clamp(occupied, 0, face.Area);
    }

    /// <summary>
    /// Sólidos con los que un vecino tapa caras: los muros básicos solo con su núcleo, los muros cortina y
    /// apilados ninguno (los apilados tapan con sus muros miembro, que el colector trae aparte).
    /// </summary>
    private static IEnumerable<Solid> GetContactSolids(Element neighbor)
    {
        try
        {
            if (neighbor is not Wall wall)
                return GetSolids(neighbor);

            return wall.WallType.Kind == WallKind.Basic
                ? WallCoreSolids(wall, GetSolids(wall))
                : Array.Empty<Solid>();
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Geometría ilegible: el vecino no descuenta
            return Array.Empty<Solid>();
        }
    }

    /// <summary>
    /// Recorta los sólidos del muro a la franja de su núcleo, quitando las capas de acabado de ambos lados.
    /// Muros curvos o sin núcleo definido se devuelven completos.
    /// </summary>
    private static IReadOnlyList<Solid> WallCoreSolids(Wall wall, IReadOnlyList<Solid> solids)
    {
        CompoundStructure? structure = wall.WallType.GetCompoundStructure();
        if (structure == null || wall.Location is not LocationCurve { Curve: Line })
            return solids;

        int first = structure.GetFirstCoreLayerIndex();
        int last = structure.GetLastCoreLayerIndex();
        if (first < 0 || last < first)
            return solids;

        // Las capas van del exterior al interior
        IList<CompoundStructureLayer> layers = structure.GetLayers();
        double coreStart = layers.Take(first).Sum(layer => layer.Width);
        double coreEnd = layers.Take(last + 1).Sum(layer => layer.Width);
        if (coreStart <= 0 && coreEnd >= structure.GetWidth())
            return solids;

        PlanarFace? exterior = ExteriorFace(wall);
        if (exterior == null)
            return solids;

        // Cada medio espacio conserva el lado al que apunta su normal
        XYZ outward = exterior.FaceNormal;
        Plane insideExteriorFinish = Plane.CreateByNormalAndOrigin(outward.Negate(), exterior.Origin - outward * coreStart);
        Plane outsideInteriorFinish = Plane.CreateByNormalAndOrigin(outward, exterior.Origin - outward * coreEnd);

        var cores = new List<Solid>();
        foreach (Solid solid in solids)
        {
            try
            {
                Solid? core = BooleanOperationsUtils.CutWithHalfSpace(solid, insideExteriorFinish);
                if (core != null)
                    core = BooleanOperationsUtils.CutWithHalfSpace(core, outsideInteriorFinish);
                if (core != null && core.Volume > 0)
                    cores.Add(core);
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Revit no pudo recortar el muro: descuenta completo antes que no descontar
                cores.Add(solid);
            }
        }

        return cores;
    }

    private static PlanarFace? ExteriorFace(Wall wall) =>
        HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior)
            .Select(reference => wall.GetGeometryObjectFromReference(reference))
            .OfType<PlanarFace>()
            .FirstOrDefault();

    private static IReadOnlyList<Solid> GetSolids(Element element)
    {
        var solids = new List<Solid>();
        AddSolids(element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }), solids);
        return solids;
    }

    private static void AddSolids(GeometryElement? geometry, List<Solid> solids)
    {
        if (geometry == null)
            return;

        foreach (GeometryObject obj in geometry)
        {
            if (obj is Solid { Volume: > 0 } solid)
                solids.Add(solid);
            // Elementos sin cortes ni uniones traen su geometría en la instancia, ya en coordenadas del proyecto
            else if (obj is GeometryInstance instance)
                AddSolids(instance.GetInstanceGeometry(), solids);
        }
    }
}
