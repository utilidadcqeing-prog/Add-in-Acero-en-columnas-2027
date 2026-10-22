namespace practicacq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

/// <summary>
/// TEMPORAL -- mediciones contra Revit real para el issue #139 (en el borde
/// del lote, los ganchos doblan hacia dentro del proyecto). Antes de usar la
/// línea de propiedad en el add-in, confirma:
///  1. que <see cref="PropertyLine.GetBoundary"/> (API pública) devuelve el
///     contorno dibujado, en qué coordenadas y a qué altura;
///  2. si Revit le da geometría en la vista 3D activa (para decidir si la
///     vista previa 3D puede apoyarse en Revit o la tiene que dibujar el
///     add-in);
///  3. para las columnas seleccionadas, qué caras quedan sobre la línea.
/// Solo lee, no cambia el modelo. Ver
/// docs/planes/2026-10-22-ganchos-zapata-y-lindero-138-139.md. Sacar una vez
/// implementado el #139.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class ComandoDiagnosticoLineaPropiedad : IExternalCommand
{
    /// <summary>Una cara está "sobre el lindero" si queda a menos de esto de la línea.</summary>
    private const double ToleranciaMetros = 0.02;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;
        var sb = new StringBuilder();

        List<PropertyLine> lineas = new FilteredElementCollector(doc)
            .OfClass(typeof(PropertyLine)).Cast<PropertyLine>().ToList();
        int segmentos = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_SitePropertyLineSegment).WhereElementIsNotElementType().GetElementCount();

        sb.AppendLine($"=== LÍNEAS DE PROPIEDAD (PropertyLine): {lineas.Count} -- segmentos sueltos en la categoría: {segmentos} ===");

        var tramos = new List<(XYZ A, XYZ B)>();
        foreach (PropertyLine linea in lineas)
        {
            sb.AppendLine();
            sb.AppendLine($"Id {linea.Id}  categoría '{linea.Category?.Name}'  por boceto: {linea.IsSketchBased()}  " +
                          $"cerrada: {linea.IsClosedLoop()}  área: {(linea.Area > 0 ? $"{UnitUtils.ConvertFromInternalUnits(linea.Area, UnitTypeId.SquareMeters):0.00} m²" : "-")}");

            IList<CurveLoop> contorno = linea.GetBoundary();
            for (int i = 0; i < contorno.Count; i++)
            {
                sb.AppendLine($"  Lazo {i}:");
                foreach (Curve curva in contorno[i])
                {
                    XYZ a = curva.GetEndPoint(0), b = curva.GetEndPoint(1);
                    sb.AppendLine($"    {curva.GetType().Name,-8} {Punto(a)} -> {Punto(b)}  largo {Metros(curva.Length):0.000}");
                    tramos.Add((a, b));
                }
            }

            DescribirGeometria(linea, new Options(), "sin vista", sb);
            if (doc.ActiveView is View vista)
                DescribirGeometria(linea, new Options { View = vista }, $"vista activa '{vista.Name}' ({vista.ViewType})", sb);
        }

        List<FamilyInstance> columnas = uidoc.Selection.GetElementIds()
            .Select(doc.GetElement).OfType<FamilyInstance>()
            .Where(c => c.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralColumns)
            .ToList();

        sb.AppendLine();
        sb.AppendLine($"=== COLUMNAS SELECCIONADAS: {columnas.Count} (caja envolvente en planta; tolerancia {ToleranciaMetros * 100:0} cm) ===");
        foreach (FamilyInstance columna in columnas)
            DescribirCaras(columna, tramos, sb);

        DiagnosticoUI.MostrarTexto(sb.ToString(), "Diagnóstico línea de propiedad (metros)");
        return Result.Succeeded;
    }

    private static void DescribirGeometria(Element el, Options opciones, string titulo, StringBuilder sb)
    {
        GeometryElement? geometria = el.get_Geometry(opciones);
        if (geometria is null)
        {
            sb.AppendLine($"  Geometría ({titulo}): ninguna");
            return;
        }

        var tipos = geometria.Select(g => g.GetType().Name).GroupBy(n => n).Select(g => $"{g.Count()} {g.Key}");
        sb.AppendLine($"  Geometría ({titulo}): {string.Join(", ", tipos)}");
    }

    /// <summary>
    /// Para cada cara de la caja envolvente de la columna (±X, ±Y), el tramo
    /// de línea paralelo más cercano que la enfrenta hacia afuera, y a qué
    /// distancia.
    /// </summary>
    private static void DescribirCaras(FamilyInstance columna, List<(XYZ A, XYZ B)> tramos, StringBuilder sb)
    {
        BoundingBoxXYZ? caja = columna.get_BoundingBox(null);
        if (caja is null) return;

        sb.AppendLine();
        sb.AppendLine($"Columna {columna.Id}  X {Metros(caja.Min.X):0.000}..{Metros(caja.Max.X):0.000}  " +
                      $"Y {Metros(caja.Min.Y):0.000}..{Metros(caja.Max.Y):0.000}  Z {Metros(caja.Min.Z):0.000}..{Metros(caja.Max.Z):0.000}");

        var caras = new (string Nombre, bool EnX, double Coord, double Sentido, double Desde, double Hasta)[]
        {
            ("+X", true, caja.Max.X, 1, caja.Min.Y, caja.Max.Y),
            ("-X", true, caja.Min.X, -1, caja.Min.Y, caja.Max.Y),
            ("+Y", false, caja.Max.Y, 1, caja.Min.X, caja.Max.X),
            ("-Y", false, caja.Min.Y, -1, caja.Min.X, caja.Max.X),
        };

        foreach (var cara in caras)
        {
            double? mejor = null;
            foreach ((XYZ a, XYZ b) in tramos)
            {
                // Tramo paralelo a la cara: constante en X para las caras ±X, en Y para ±Y.
                double fijoA = cara.EnX ? a.X : a.Y, fijoB = cara.EnX ? b.X : b.Y;
                if (Math.Abs(fijoA - fijoB) > 1e-6) continue;

                double libreA = cara.EnX ? a.Y : a.X, libreB = cara.EnX ? b.Y : b.X;
                if (Math.Max(libreA, libreB) < cara.Desde || Math.Min(libreA, libreB) > cara.Hasta) continue;

                double distancia = (fijoA - cara.Coord) * cara.Sentido;
                // Una línea detrás de la cara es la del lado opuesto de la columna, no esta.
                if (Metros(distancia) < -ToleranciaMetros) continue;
                if (mejor is null || Math.Abs(distancia) < Math.Abs(mejor.Value)) mejor = distancia;
            }

            string texto = mejor is double d
                ? $"línea a {Metros(d) * 100:0.0} cm{(Math.Abs(Metros(d)) <= ToleranciaMetros ? "  <-- SOBRE EL LINDERO" : "")}"
                : "sin línea paralela enfrente";
            sb.AppendLine($"  Cara {cara.Nombre}: {texto}");
        }
    }

    private static string Punto(XYZ p) => $"({Metros(p.X):0.000}; {Metros(p.Y):0.000}; {Metros(p.Z):0.000})";

    private static double Metros(double pies) => UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Meters);
}
