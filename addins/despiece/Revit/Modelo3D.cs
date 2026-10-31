using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace CQEING.Despiece.Revit;

/// <summary>
/// Lee lo que dibuja el 3D de la página: el eje de cada barra y los triángulos de la viga, en metros y centrados en la viga.
/// Solo lee: no abre transacción ni crea nada en el proyecto.
/// </summary>
internal static class Modelo3D
{
    private const double Metros = 0.3048;

    public static object Leer(Document doc, VigaContinua viga)
    {
        List<BoundingBoxXYZ> cajas = viga.Vigas.Select(id => doc.GetElement(id).get_BoundingBox(null)).ToList();
        XYZ centro = new XYZ(cajas.Min(c => c.Min.X) + cajas.Max(c => c.Max.X), cajas.Min(c => c.Min.Y) + cajas.Max(c => c.Max.Y), cajas.Min(c => c.Min.Z) + cajas.Max(c => c.Max.Z)) / 2;

        var barras = new List<object>();
        foreach (Rebar b in new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>().Where(b => viga.Vigas.Contains(b.GetHostId())))
        {
            string tipo = doc.GetElement(b.GetTypeId()).Name;
            double radio = doc.GetElement(b.GetTypeId()) is RebarBarType t ? t.BarNominalDiameter / 2 * Metros : 0.005;
            for (int i = 0; i < b.Quantity; i++)
            {
                if (b.DoesBarExistAtPosition(i))
                    barras.Add(new { c = LectorDespiece.Color(tipo), r = radio, p = Puntos(b, i, centro) });
            }
        }

        var triangulos = new List<double>();
        foreach (ElementId id in viga.Vigas)
            Triangulos(doc.GetElement(id).get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }), centro, triangulos);
        return new { barras, viga = triangulos };
    }

    /// <summary>El eje de una barra del conjunto, como x, y, z seguidos, sin puntos repetidos donde se tocan dos tramos.</summary>
    private static List<double> Puntos(Rebar b, int indice, XYZ centro)
    {
        var puntos = new List<double>();
        XYZ? anterior = null;
        foreach (Curve curva in b.GetTransformedCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, indice))
        {
            foreach (XYZ punto in curva.Tessellate())
            {
                if (anterior is not null && punto.IsAlmostEqualTo(anterior))
                    continue;
                Agregar(puntos, punto, centro);
                anterior = punto;
            }
        }
        return puntos;
    }

    private static void Triangulos(GeometryElement geometria, XYZ centro, List<double> salida)
    {
        foreach (GeometryObject objeto in geometria)
        {
            if (objeto is GeometryInstance instancia)
                Triangulos(instancia.GetInstanceGeometry(), centro, salida);
            else if (objeto is Solid { Volume: > 0 } solido)
            {
                foreach (Face cara in solido.Faces)
                {
                    Mesh malla = cara.Triangulate();
                    for (int i = 0; i < malla.NumTriangles; i++)
                    {
                        MeshTriangle triangulo = malla.get_Triangle(i);
                        for (int v = 0; v < 3; v++)
                            Agregar(salida, triangulo.get_Vertex(v), centro);
                    }
                }
            }
        }
    }

    private static void Agregar(List<double> salida, XYZ punto, XYZ centro)
    {
        XYZ p = (punto - centro) * Metros;
        salida.Add(Math.Round(p.X, 4));
        salida.Add(Math.Round(p.Y, 4));
        salida.Add(Math.Round(p.Z, 4));
    }
}
