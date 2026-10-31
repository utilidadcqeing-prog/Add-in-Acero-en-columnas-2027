using Autodesk.Revit.DB;
using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Viguetas;

namespace CQEING.Vigas.Revit;

/// <summary>
/// Adaptador de Revit para «Crear viguetas» (#948): los niveles que tienen armazón estructural y, por nivel, sus
/// vigas y viguetas (eje por el centro de la geometría y ancho medido, como <see cref="LectorModelo"/>) y sus columnas.
/// Todo en mm.
/// </summary>
internal static class LectorPiso
{
    /// <summary>Lo que escribe el add-in en Comentarios de cada vigueta que crea (decisión 7 del plan).</summary>
    public const string Marca = "CQEING viguetas";

    /// <summary>Una columna cuenta en el piso si llega a menos de esto (mm) de la cota del nivel.</summary>
    private const double ToleranciaColumna = 100;

    public static List<PisoResumen> Pisos(Document doc) =>
        Armazon(doc).Select(NivelDe).OfType<Level>()
            .GroupBy(l => l.Id)
            .Select(g => new PisoResumen(g.Key.ToString(), g.First().Name, LectorModelo.Mm(g.First().Elevation), g.Count()))
            .OrderBy(p => p.Cota)
            .ToList();

    public static PisoLeido Leer(Document doc, Level nivel)
    {
        var armazon = new List<ArmazonPiso>();
        foreach (FamilyInstance f in Armazon(doc).Where(f => NivelDe(f)?.Id == nivel.Id))
            if (Leer(f) is ArmazonPiso a)
                armazon.Add(a);

        double cota = LectorModelo.Mm(nivel.Elevation);
        var columnas = new List<ColumnaPiso>();
        foreach (FamilyInstance c in Instancias(doc, BuiltInCategory.OST_StructuralColumns))
        {
            List<XYZ> puntos = LectorModelo.Puntos(c);
            if (puntos.Count == 0)
                continue;
            double abajo = LectorModelo.Mm(puntos.Min(p => p.Z)), arriba = LectorModelo.Mm(puntos.Max(p => p.Z));
            if (abajo - ToleranciaColumna <= cota && cota <= arriba + ToleranciaColumna)
                columnas.Add(new ColumnaPiso(c.Id.ToString(),
                    new Punto(LectorModelo.Mm(puntos.Min(p => p.X)), LectorModelo.Mm(puntos.Min(p => p.Y))),
                    new Punto(LectorModelo.Mm(puntos.Max(p => p.X)), LectorModelo.Mm(puntos.Max(p => p.Y)))));
        }
        return new PisoLeido(nivel.Id.ToString(), nivel.Name, cota, armazon, columnas);
    }

    /// <summary>Los tipos de armazón del modelo, para elegir con cuál se crean las viguetas (decisión 8).</summary>
    public static List<TipoArmazon> Tipos(Document doc) =>
        new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsElementType()
            .OfType<FamilySymbol>()
            .Select(t => new TipoArmazon(t.Id.ToString(), $"{t.FamilyName} : {t.Name}", ReglaVigueta.PorNombre(t.Name)))
            .OrderByDescending(t => t.Sugerido).ThenBy(t => t.Nombre)
            .ToList();

    /// <summary>
    /// La cota (pies) de las líneas de ubicación del armazón del nivel que no creó el add-in: las viguetas nuevas van a
    /// la misma altura que las vigas que ya están.
    /// </summary>
    public static double CotaLineas(Document doc, Level nivel)
    {
        List<double> z = Armazon(doc).Where(f => NivelDe(f)?.Id == nivel.Id && !EsCreada(f))
            .Select(f => f.Location).OfType<LocationCurve>().Select(l => l.Curve.GetEndPoint(0).Z).ToList();
        return z.Count == 0 ? nivel.Elevation : z.Average();
    }

    /// <summary>Las viguetas que creó el add-in, por id.</summary>
    public static bool EsCreada(Element e) =>
        e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == Marca;

    private static ArmazonPiso? Leer(FamilyInstance f)
    {
        if (f.Location is not LocationCurve { Curve: Line linea })
            return null;
        List<XYZ> puntos = LectorModelo.Puntos(f);
        if (puntos.Count == 0)
            return null;
        XYZ d = linea.Direction;
        var normal = new XYZ(-d.Y, d.X, 0).Normalize();
        double ancho = puntos.Max(p => p.DotProduct(normal)) - puntos.Min(p => p.DotProduct(normal));
        // La línea de ubicación puede ir por una cara (justificación): se corre al centro de la geometría.
        var centro = new XYZ((puntos.Min(p => p.X) + puntos.Max(p => p.X)) / 2, (puntos.Min(p => p.Y) + puntos.Max(p => p.Y)) / 2, 0);
        XYZ p0 = linea.GetEndPoint(0), p1 = linea.GetEndPoint(1);
        XYZ corrimiento = normal * (centro - new XYZ(p0.X, p0.Y, 0)).DotProduct(normal);
        return new ArmazonPiso(f.Id.ToString(), Planta(p0 + corrimiento), Planta(p1 + corrimiento), LectorModelo.Mm(ancho),
            f.Symbol.Name, EsCreada(f));
    }

    private static IEnumerable<FamilyInstance> Armazon(Document doc) =>
        Instancias(doc, BuiltInCategory.OST_StructuralFraming).Where(f => f.Location is LocationCurve { Curve: Line });

    private static Level? NivelDe(FamilyInstance f) =>
        f.Document.GetElement(f.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId() ?? ElementId.InvalidElementId) as Level;

    private static IEnumerable<FamilyInstance> Instancias(Document doc, BuiltInCategory categoria) =>
        new FilteredElementCollector(doc).OfCategory(categoria).WhereElementIsNotElementType().OfType<FamilyInstance>();

    private static Punto Planta(XYZ p) => new(LectorModelo.Mm(p.X), LectorModelo.Mm(p.Y));
}
