using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Revit;

/// <summary>
/// Adaptador de Revit (#81): convierte ejes, vigas, columnas y muros de carga del documento al
/// <see cref="ModeloLeido"/> del motor, en mm. Alto, ancho y huellas salen de la geometría, no de parámetros de la
/// familia (el modelo de prueba mostró que coinciden con b y h, pero cada familia los puede llamar distinto).
/// </summary>
internal static class LectorModelo
{
    public static ModeloLeido Leer(Document doc) => new(Ejes(doc), Vigas(doc), Apoyos(doc));

    private static List<EjeLeido> Ejes(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
            .Where(g => g.Curve is Line)
            .Select(g => new EjeLeido(g.Name, Planta(g.Curve.GetEndPoint(0)), Planta(g.Curve.GetEndPoint(1))))
            .ToList();

    private static List<VigaLeida> Vigas(Document doc)
    {
        var vigas = new List<VigaLeida>();
        foreach (FamilyInstance viga in Instancias(doc, BuiltInCategory.OST_StructuralFraming))
        {
            if (viga.Location is not LocationCurve { Curve: Line linea })
                continue;
            List<XYZ> puntos = Puntos(viga);
            if (puntos.Count == 0)
                continue;
            XYZ d = linea.Direction;
            var normal = new XYZ(-d.Y, d.X, 0).Normalize();
            double ancho = puntos.Max(p => p.DotProduct(normal)) - puntos.Min(p => p.DotProduct(normal));
            NivelLeido? nivel = doc.GetElement(viga.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId()
                ?? ElementId.InvalidElementId) is Level l ? new NivelLeido(l.Name, Mm(l.Elevation)) : null;
            vigas.Add(new VigaLeida(viga.Id.ToString(), Planta(linea.GetEndPoint(0)), Planta(linea.GetEndPoint(1)),
                Mm(puntos.Min(p => p.Z)), Mm(puntos.Max(p => p.Z)), Mm(ancho), nivel));
        }
        return vigas;
    }

    private static List<ApoyoLeido> Apoyos(Document doc)
    {
        var apoyos = new List<ApoyoLeido>();
        foreach (FamilyInstance columna in Instancias(doc, BuiltInCategory.OST_StructuralColumns))
            if (Apoyo(columna, TipoApoyo.Columna, null) is ApoyoLeido a)
                apoyos.Add(a);

        // Solo muros de carga (decisión 4 del plan): los de cerramiento o división no apoyan vigas.
        foreach (Wall muro in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>())
        {
            if (muro.StructuralUsage == StructuralWallUsage.NonBearing)
                continue;
            Punto? direccion = muro.Location is LocationCurve { Curve: Line l } ? new Punto(l.Direction.X, l.Direction.Y) : null;
            if (Apoyo(muro, TipoApoyo.Muro, direccion) is ApoyoLeido a)
                apoyos.Add(a);
        }
        return apoyos;
    }

    private static ApoyoLeido? Apoyo(Element e, TipoApoyo tipo, Punto? direccion)
    {
        List<XYZ> puntos = Puntos(e);
        if (puntos.Count == 0)
            return null;
        List<Punto> huella = puntos.Select(Planta).Distinct().ToList();
        return new ApoyoLeido(e.Id.ToString(), tipo, huella, Mm(puntos.Min(p => p.Z)), Mm(puntos.Max(p => p.Z)), direccion);
    }

    private static IEnumerable<FamilyInstance> Instancias(Document doc, BuiltInCategory categoria) =>
        new FilteredElementCollector(doc).OfCategory(categoria).WhereElementIsNotElementType().OfType<FamilyInstance>();

    /// <summary>Puntos de las aristas de los sólidos del elemento (también dentro de instancias de familia).</summary>
    public static List<XYZ> Puntos(Element e) => Puntos(e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })).ToList();

    private static IEnumerable<XYZ> Puntos(GeometryElement? geometria)
    {
        if (geometria is null)
            yield break;
        foreach (GeometryObject objeto in geometria)
        {
            if (objeto is Solid solido && solido.Volume > 0)
            {
                foreach (Edge arista in solido.Edges)
                    foreach (XYZ p in arista.Tessellate())
                        yield return p;
            }
            else if (objeto is GeometryInstance instancia)
            {
                foreach (XYZ p in Puntos(instancia.GetInstanceGeometry()))
                    yield return p;
            }
        }
    }

    private static Punto Planta(XYZ p) => new(Math.Round(Mm(p.X), 1), Math.Round(Mm(p.Y), 1));

    public static double Mm(double piesInternos) => UnitUtils.ConvertFromInternalUnits(piesInternos, UnitTypeId.Millimeters);
}
