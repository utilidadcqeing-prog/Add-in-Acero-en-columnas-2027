using Autodesk.Revit.DB;
using CQEING.Despiece.Calculo;

namespace CQEING.Despiece.Revit;

/// <summary>La imagen de una planta y dónde cae cada viga en ella, en píxeles.</summary>
internal sealed record PlantaGenerada(string Ruta, int Ancho, int Alto, IReadOnlyDictionary<string, CajaPixeles> Cajas);

/// <summary>
/// Exporta una planta real de Revit para elegir vigas sobre ella (#221). Trabaja en una copia temporal de la vista y
/// deshace todo al terminar: el proyecto queda como estaba.
/// </summary>
internal static class GeneradorPlanta
{
    private const double PiesPorMetro = 1 / 0.3048;

    /// <summary>Lo que se deja alrededor de las vigas en el recorte. Las cruces de calibración quedan adentro.</summary>
    private const double Margen = 2.0;
    private const double MargenMarca = 1.0;
    private const double LargoMarca = 0.25;

    public static PlantaGenerada Generar(Document doc, ViewPlan planta, string carpeta)
    {
        Registro.Paso("planta: empieza " + planta.Name);
        using var t = new Transaction(doc, "Vista previa de planta");
        t.Start();
        try
        {
            var copia = (ViewPlan)doc.GetElement(planta.Duplicate(ViewDuplicateOption.Duplicate));
            copia.Name = "CQEING temporal " + Guid.NewGuid().ToString("N");
            copia.ViewTemplateId = ElementId.InvalidElementId;
            copia.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Sections), true);

            List<Element> vigas = new FilteredElementCollector(doc, copia.Id).OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType().ToList();
            if (vigas.Count == 0)
                return Exportar(doc, copia, carpeta, [], null, null);

            BoundingBoxXYZ recorte = copia.CropBox;
            Transform aVista = recorte.Transform.Inverse;
            var cajas = vigas.Select(v => (Viga: v, Caja: CajaEnVista(v, aVista))).ToList();
            double x0 = cajas.Min(c => c.Caja.Min.X), y0 = cajas.Min(c => c.Caja.Min.Y);
            double x1 = cajas.Max(c => c.Caja.Max.X), y1 = cajas.Max(c => c.Caja.Max.Y);
            recorte.Min = new XYZ(x0 - Margen, y0 - Margen, recorte.Min.Z);
            recorte.Max = new XYZ(x1 + Margen, y1 + Margen, recorte.Max.Z);
            copia.CropBoxActive = true;
            copia.CropBox = recorte;
            copia.CropBoxVisible = false;
            doc.Regenerate();

            var marcaA = new PuntoModelo(x0 - MargenMarca, y0 - MargenMarca);
            var marcaB = new PuntoModelo(x1 + MargenMarca, y1 + MargenMarca);
            Marcar(doc, copia, recorte.Transform, marcaA);
            Marcar(doc, copia, recorte.Transform, marcaB);
            doc.Regenerate();
            return Exportar(doc, copia, carpeta, cajas, marcaA, marcaB);
        }
        finally
        {
            Registro.Paso("planta: deshaciendo");
            t.RollBack();
            Registro.Paso("planta: deshecha");
        }
    }

    private static PlantaGenerada Exportar(Document doc, View copia, string carpeta,
        List<(Element Viga, (XYZ Min, XYZ Max) Caja)> cajas, PuntoModelo? marcaA, PuntoModelo? marcaB)
    {
        Registro.Paso("planta: exportando");
        string ruta = ExportadorImagen.Exportar(doc, copia, carpeta, ExportadorImagen.AnchoPlanta);
        Registro.Paso("planta: exportada, calibrando");
        if (marcaA is null || marcaB is null)
        {
            var (_, a, h) = ExportadorImagen.Calibrar(ruta, new PuntoModelo(0, 0), new PuntoModelo(1, 1));
            return new PlantaGenerada(ruta, a, h, new Dictionary<string, CajaPixeles>());
        }
        var (calibracion, ancho, alto) = ExportadorImagen.Calibrar(ruta, marcaA, marcaB);
        var enPixeles = new Dictionary<string, CajaPixeles>();
        if (calibracion is not null)
            foreach ((Element viga, (XYZ min, XYZ max)) in cajas)
                enPixeles[viga.Id.ToString()] = calibracion.ACaja(new PuntoModelo(min.X, min.Y), new PuntoModelo(max.X, max.Y));
        return new PlantaGenerada(ruta, ancho, alto, enPixeles);
    }

    /// <summary>La caja de la viga en coordenadas del recorte: la línea de ubicación va por un borde, no por el centro.</summary>
    private static (XYZ Min, XYZ Max) CajaEnVista(Element viga, Transform aVista)
    {
        BoundingBoxXYZ c = viga.get_BoundingBox(null);
        XYZ a = aVista.OfPoint(c.Min), b = aVista.OfPoint(c.Max);
        return (new XYZ(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), 0), new XYZ(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), 0));
    }

    private static void Marcar(Document doc, View vista, Transform aModelo, PuntoModelo p)
    {
        var magenta = new OverrideGraphicSettings();
        magenta.SetProjectionLineColor(new Color(255, 0, 255));
        magenta.SetProjectionLineWeight(1);
        double z = vista.Origin.Z;
        XYZ En(double x, double y)
        {
            XYZ q = aModelo.OfPoint(new XYZ(x, y, 0));
            return new XYZ(q.X, q.Y, z);
        }
        foreach (Line l in new[]
        {
            Line.CreateBound(En(p.X - LargoMarca, p.Y), En(p.X + LargoMarca, p.Y)),
            Line.CreateBound(En(p.X, p.Y - LargoMarca), En(p.X, p.Y + LargoMarca)),
        })
            vista.SetElementOverrides(doc.Create.NewDetailCurve(vista, l).Id, magenta);
    }
}
