using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CQEING.Despiece.Calculo;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Despiece.Revit;

/// <summary>Una viga continua: las vigas seguidas sobre una misma línea y nivel, con el mismo tipo.</summary>
internal sealed record VigaContinua(ElementoLeido Elemento, IReadOnlyList<ElementId> Vigas, XYZ Inicio, XYZ Direccion);

/// <summary>Lee del modelo lo que muestra el despiece (#221).</summary>
internal static class LectorDespiece
{
    private const double PiesPorMetro = 1 / 0.3048;

    /// <summary>Hasta aquí se unen dos vigas seguidas: lo que deja una columna entre ellas.</summary>
    private const double HuecoMaximo = 1.0 * PiesPorMetro;

    /// <summary>Hasta aquí una viga está "sobre" un eje o sobre la misma línea que otra.</summary>
    private const double Cercania = 0.3 * PiesPorMetro;

    public static double Mm(double pies) => UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Millimeters);

    /// <summary>Las plantas estructurales, de abajo hacia arriba.</summary>
    public static List<ViewPlan> Plantas(Document doc) => new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
        .Where(v => !v.IsTemplate && v.ViewType == ViewType.EngineeringPlan && v.GenLevel is not null)
        .OrderBy(v => v.GenLevel.Elevation).ThenBy(v => v.Name, StringComparer.Ordinal).ToList();

    public static List<VigaContinua> Leer(Document doc)
    {
        List<FamilyInstance> vigas = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
            .WhereElementIsNotElementType().OfType<FamilyInstance>().Where(v => v.Location is LocationCurve { Curve: Line }).ToList();
        ILookup<ElementId, Rebar> barras = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>().ToLookup(b => b.GetHostId());
        List<Grid> ejes = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().Where(g => g.Curve is Line).ToList();
        List<Level> niveles = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();

        var continuas = new List<VigaContinua>();
        foreach (List<FamilyInstance> tramo in Tramos(vigas))
            continuas.Add(Continua(doc, tramo, barras, ejes, niveles));
        return continuas;
    }

    private static IEnumerable<List<FamilyInstance>> Tramos(List<FamilyInstance> vigas)
    {
        var pendientes = vigas.OrderBy(v => Linea(v).GetEndPoint(0).X).ThenBy(v => Linea(v).GetEndPoint(0).Y).ToList();
        while (pendientes.Count > 0)
        {
            FamilyInstance primera = pendientes[0];
            Line linea = Linea(primera);
            XYZ dir = Direccion(linea);
            List<FamilyInstance> misma = pendientes.Where(v => v.GetTypeId() == primera.GetTypeId()
                    && Math.Abs(Linea(v).GetEndPoint(0).Z - linea.GetEndPoint(0).Z) < Cercania
                    && Direccion(Linea(v)).IsAlmostEqualTo(dir)
                    && Fuera(Linea(v).GetEndPoint(0), linea.GetEndPoint(0), dir) < Cercania)
                .OrderBy(v => Min(Linea(v), dir)).ToList();
            var tramo = new List<FamilyInstance> { misma[0] };
            double fin = Max(Linea(misma[0]), dir);
            foreach (FamilyInstance v in misma.Skip(1))
            {
                if (Min(Linea(v), dir) - fin > HuecoMaximo)
                    break;
                tramo.Add(v);
                fin = Math.Max(fin, Max(Linea(v), dir));
            }
            pendientes.RemoveAll(tramo.Contains);
            yield return tramo;
        }
    }

    private static VigaContinua Continua(Document doc, List<FamilyInstance> tramo, ILookup<ElementId, Rebar> barras, List<Grid> ejes, List<Level> niveles)
    {
        Line linea = Linea(tramo[0]);
        XYZ dir = Direccion(linea);
        double inicio = tramo.Min(v => Min(Linea(v), dir)), fin = tramo.Max(v => Max(Linea(v), dir));
        XYZ punto = linea.GetEndPoint(0);
        XYZ origen = punto + dir * (inicio - punto.DotProduct(dir));
        var lista = new List<BarraLeida>();
        foreach (Rebar b in tramo.SelectMany(v => barras[v.Id]))
        {
            BoundingBoxXYZ caja = b.get_BoundingBox(null);
            double centro = ((caja.Min + caja.Max) / 2).DotProduct(dir) - inicio;
            lista.Add(new BarraLeida(doc.GetElement(b.GetTypeId()).Name, doc.GetElement(b.GetShapeId())?.Name ?? "", b.Quantity,
                (int)Math.Round(Mm(LargoBarra(b))), (int)Math.Round(Mm(centro)),
                (int)Math.Round(Mm(b.GetCenterlineCurves(false, true, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0).Sum(c => c.Length)))));
        }
        BoundingBoxXYZ cv = tramo[0].get_BoundingBox(null);
        XYZ perp = XYZ.BasisZ.CrossProduct(dir);
        int ancho = (int)Math.Round(Mm(Math.Abs((cv.Max - cv.Min).DotProduct(perp))));
        int alto = (int)Math.Round(Mm(cv.Max.Z - cv.Min.Z));
        string eje = ejes.FirstOrDefault(g => Direccion((Line)g.Curve).IsAlmostEqualTo(dir) && Fuera(g.Curve.GetEndPoint(0), punto, dir) < Cercania)?.Name ?? "";
        // Por la altura de la viga: en estas vigas el nivel del elemento viene vacío (PROY_JULIO_BETO, 30-oct).
        string nivel = niveles.OrderBy(n => Math.Abs(n.Elevation - punto.Z)).FirstOrDefault()?.Name ?? "";
        var elemento = new ElementoLeido(tramo[0].Id.ToString(), eje, ancho, alto, (int)Math.Round(Mm(fin - inicio)), lista, nivel, TipoElemento.EsVigueta(tramo[0].Symbol.Name, ancho));
        return new VigaContinua(elemento, tramo.Select(v => v.Id).ToList(), origen, dir);
    }

    /// <summary>
    /// El largo de una barra del conjunto, con ganchos (ver #217: no sirve el total del conjunto), como lo dicen sus
    /// cotas: cada gancho de 90° suma lo que Revit no cuenta en la esquina, <c>2 (R + d/2) − R·π/2</c> (la misma cuenta
    /// de <c>GanchoPropio.DiferenciaCotas</c> en acero de vigas).
    /// </summary>
    public static double LargoBarra(Rebar b)
    {
        double largo = b.get_Parameter(BuiltInParameter.REBAR_ELEM_LENGTH)?.AsDouble() ?? 0;
        if (b.Document.GetElement(b.GetTypeId()) is not RebarBarType barra)
            return largo;
        int ganchos = new[] { 0, 1 }.Count(punta => b.Document.GetElement(b.GetHookTypeId(punta)) is RebarHookType gancho && Math.Abs(gancho.HookAngle - Math.PI / 2) < 1e-3);
        double d = barra.BarNominalDiameter;
        return TextosDespiece.LargoPorCotas(largo, barra.StandardHookBendDiameter / 2 + d / 2, d, ganchos);
    }

    /// <summary>El número de barra, del nombre de su tipo ("#5 CQEING" da 5); 0 si el nombre no lo dice.</summary>
    public static int Numero(string tipo) => TextosDespiece.Numero(tipo);

    public static string Color(string tipo) => ColoresDiametro.De(Numero(tipo));

    /// <summary>La etiqueta de una barra: <c>3 #5 L= 6.00</c>, o <c>3 #5 L= 5.50/6.00</c> si se da su tramo recto.</summary>
    public static string Etiqueta(string tipo, int cantidad, double largoPies, double? rectoPies = null) => TextosDespiece.Barra(tipo, cantidad, largoPies * 0.3048, rectoPies * 0.3048);

    /// <summary>La separación de un conjunto de estribos, en pies; 0 si es uno solo.</summary>
    public static double Separacion(Rebar b) => b.Quantity > 1 ? b.get_Parameter(BuiltInParameter.REBAR_ELEM_BAR_SPACING)?.AsDouble() ?? 0 : 0;

    /// <summary>La etiqueta de una zona de estribos, como en el plano: <c>25 #4 @0.10</c>. Uno solo no lleva separación.</summary>
    public static string EtiquetaEstribos(string tipo, int cantidad, double separacionPies)
    {
        int n = Numero(tipo);
        string nombre = n > 0 ? "#" + n : tipo;
        return separacionPies > 0
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} @{2:0.00}", cantidad, nombre, separacionPies * 0.3048)
            : $"{cantidad} {nombre}";
    }

    private static Line Linea(FamilyInstance v) => (Line)((LocationCurve)v.Location).Curve;

    /// <summary>La dirección con el sentido normalizado: dos vigas dibujadas al revés quedan iguales.</summary>
    private static XYZ Direccion(Line l)
    {
        XYZ d = l.Direction;
        d = new XYZ(d.X, d.Y, 0).Normalize();
        return d.X < -1e-6 || (Math.Abs(d.X) < 1e-6 && d.Y < 0) ? -d : d;
    }

    private static double Min(Line l, XYZ d) => Math.Min(l.GetEndPoint(0).DotProduct(d), l.GetEndPoint(1).DotProduct(d));

    private static double Max(Line l, XYZ d) => Math.Max(l.GetEndPoint(0).DotProduct(d), l.GetEndPoint(1).DotProduct(d));

    /// <summary>Qué tan lejos está un punto de la línea que pasa por <paramref name="sobre"/>, medido en planta.</summary>
    private static double Fuera(XYZ punto, XYZ sobre, XYZ dir)
    {
        XYZ v = new(punto.X - sobre.X, punto.Y - sobre.Y, 0);
        return Math.Abs(v.DotProduct(XYZ.BasisZ.CrossProduct(dir)));
    }
}
