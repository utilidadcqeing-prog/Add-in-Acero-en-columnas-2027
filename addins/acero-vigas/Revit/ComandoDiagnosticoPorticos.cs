using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Revit;

/// <summary>
/// Diagnóstico temporal (#81, regla "verificar contra Revit real"): vuelca a un archivo de texto lo que el add-in
/// lee del modelo (ejes, niveles, vigas, columnas y muros), en mm, para compararlo con el modelo antes de dar por
/// buena la lectura de pórticos. Solo lee: no modifica el documento. Plan: docs/planes/2026-10-16-leer-portico-revit-81.md.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class ComandoDiagnosticoPorticos : IExternalCommand
{
    private static readonly CultureInfo Cultura = CultureInfo.InvariantCulture;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document doc = commandData.Application.ActiveUIDocument.Document;
        var informe = new StringBuilder();
        informe.AppendLine($"Modelo: {doc.Title} · Revit {commandData.Application.Application.VersionNumber} · {DateTime.Now:yyyy-MM-dd HH:mm}");
        informe.AppendLine("Medidas en mm, coordenadas del modelo (internas).");

        EscribirEjes(doc, informe);
        EscribirNiveles(doc, informe);
        EscribirVigas(doc, informe);
        EscribirColumnas(doc, informe);
        EscribirMuros(doc, informe);
        EscribirPorticos(doc, informe);

        string ruta = Path.Combine(Path.GetTempPath(), $"CQEING-vigas-diagnostico-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(ruta, informe.ToString(), Encoding.UTF8);
        TaskDialog.Show("Pórticos (diagnóstico)", $"Informe escrito en:\n{ruta}");
        return Result.Succeeded;
    }

    private static void EscribirEjes(Document doc, StringBuilder s)
    {
        s.AppendLine().AppendLine("== EJES");
        foreach (Grid eje in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().OrderBy(g => g.Name))
        {
            Curve c = eje.Curve;
            s.AppendLine($"{eje.Name}\t{c.GetType().Name}\t{P(c.GetEndPoint(0))} -> {P(c.GetEndPoint(1))}");
        }
    }

    private static void EscribirNiveles(Document doc, StringBuilder s)
    {
        s.AppendLine().AppendLine("== NIVELES");
        foreach (Level nivel in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation))
            s.AppendLine($"{nivel.Id}\t{nivel.Name}\telev={Mm(nivel.Elevation)}");
    }

    private static void EscribirVigas(Document doc, StringBuilder s)
    {
        s.AppendLine().AppendLine("== VIGAS (OST_StructuralFraming)");
        foreach (FamilyInstance viga in Instancias(doc, BuiltInCategory.OST_StructuralFraming))
        {
            string ubicacion = viga.Location is LocationCurve lc
                ? $"{lc.Curve.GetType().Name} {P(lc.Curve.GetEndPoint(0))} -> {P(lc.Curve.GetEndPoint(1))}"
                : "sin LocationCurve";
            s.AppendLine($"{viga.Id}\t{viga.Symbol.FamilyName} : {viga.Symbol.Name}\t{viga.StructuralType}\tnivel={NombreNivel(doc, viga, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)}");
            s.AppendLine($"\tubicación: {ubicacion}");
            if (viga.Location is LocationCurve curva && curva.Curve is Line linea)
                s.AppendLine($"\tsección medida en la geometría: {SeccionMedida(viga, linea)}");
            s.AppendLine($"\tcaja: {Caja(viga)}");
            s.AppendLine($"\tlargos del tipo: {LargosDelTipo(viga.Symbol)}");
        }
    }

    private static void EscribirColumnas(Document doc, StringBuilder s)
    {
        s.AppendLine().AppendLine("== COLUMNAS (OST_StructuralColumns)");
        foreach (FamilyInstance columna in Instancias(doc, BuiltInCategory.OST_StructuralColumns))
        {
            string ubicacion = columna.Location switch
            {
                LocationPoint lp => $"punto {P(lp.Point)}",
                LocationCurve lc => $"curva {P(lc.Curve.GetEndPoint(0))} -> {P(lc.Curve.GetEndPoint(1))}",
                _ => "sin ubicación",
            };
            s.AppendLine($"{columna.Id}\t{columna.Symbol.FamilyName} : {columna.Symbol.Name}\tbase={NombreNivel(doc, columna, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)}\ttope={NombreNivel(doc, columna, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)}");
            s.AppendLine($"\t{ubicacion}\tcaja: {Caja(columna)}");
        }
    }

    private static void EscribirMuros(Document doc, StringBuilder s)
    {
        s.AppendLine().AppendLine("== MUROS");
        foreach (Wall muro in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>())
        {
            string ubicacion = muro.Location is LocationCurve lc
                ? $"{P(lc.Curve.GetEndPoint(0))} -> {P(lc.Curve.GetEndPoint(1))}"
                : "sin LocationCurve";
            s.AppendLine($"{muro.Id}\t{muro.Name}\tuso={muro.StructuralUsage}\tancho={Mm(muro.Width)}\t{ubicacion}\tcaja: {Caja(muro)}");
        }
    }

    /// <summary>Lo que el add-in arma con lo leído: los pórticos tal como los recibe el motor, y los avisos.</summary>
    private static void EscribirPorticos(Document doc, StringBuilder s)
    {
        ResultadoLectura r = AgrupadorPorticos.Agrupar(LectorModelo.Leer(doc));
        s.AppendLine().AppendLine($"== PÓRTICOS ARMADOS ({r.Porticos.Count}) · posiciones en mm desde el eje del primer apoyo");
        foreach (PorticoLeido p in r.Porticos)
        {
            s.AppendLine($"{p.Nombre}\tnivel={p.Nivel ?? "?"}\ttipo={p.Tipo}\tvoladizos={p.Portico.VoladizoInicio}/{p.Portico.VoladizoFinal}\tvigas={string.Join(",", p.Vigas)}");
            for (int i = 0; i < p.Portico.Apoyos.Count; i++)
            {
                var a = p.Portico.Apoyos[i];
                s.AppendLine($"\tapoyo {a.Eje}\t{p.TiposDeApoyo[i]}\teje={a.PosicionEje}\tcaras={a.CaraIzquierda}..{a.CaraDerecha}");
            }
            foreach (TramoLeido t in p.Tramos)
                s.AppendLine($"\t{t.Nombre}\t{t.Desde}..{t.Hasta}\tsección={t.Ancho:F0}x{t.Alto:F0}");
        }
        s.AppendLine().AppendLine("== AVISOS DE LECTURA");
        foreach (AvisoLectura a in r.Avisos)
            s.AppendLine($"{a.Tipo}\t{a.Elemento}");
    }

    private static IEnumerable<FamilyInstance> Instancias(Document doc, BuiltInCategory categoria) =>
        new FilteredElementCollector(doc).OfCategory(categoria).WhereElementIsNotElementType().OfType<FamilyInstance>();

    private static string NombreNivel(Document doc, Element e, BuiltInParameter parametro)
    {
        ElementId id = e.get_Parameter(parametro)?.AsElementId() ?? ElementId.InvalidElementId;
        return doc.GetElement(id) is Level nivel ? nivel.Name : "?";
    }

    /// <summary>
    /// Ancho y alto de la viga medidos en su geometría: se proyectan los puntos de sus aristas sobre la horizontal
    /// perpendicular al eje de la viga y sobre la vertical.
    /// </summary>
    private static string SeccionMedida(FamilyInstance viga, Line linea)
    {
        XYZ direccion = linea.Direction;
        var normal = new XYZ(-direccion.Y, direccion.X, 0).Normalize();
        List<XYZ> puntos = LectorModelo.Puntos(viga);
        if (puntos.Count == 0)
            return "sin geometría";
        double ancho = puntos.Max(p => p.DotProduct(normal)) - puntos.Min(p => p.DotProduct(normal));
        double alto = puntos.Max(p => p.Z) - puntos.Min(p => p.Z);
        return $"ancho={Mm(ancho)} alto={Mm(alto)}";
    }

    private static string LargosDelTipo(ElementType tipo) =>
        string.Join(", ", tipo.Parameters.Cast<Parameter>()
            .Where(p => p.StorageType == StorageType.Double && p.Definition.GetDataType() == SpecTypeId.Length)
            .Select(p => $"{p.Definition.Name}={Mm(p.AsDouble())}")
            .OrderBy(t => t, StringComparer.Ordinal));

    private static string Caja(Element e) =>
        e.get_BoundingBox(null) is BoundingBoxXYZ caja ? $"{P(caja.Min)} .. {P(caja.Max)}" : "sin caja";

    private static string P(XYZ p) => $"({Mm(p.X)}, {Mm(p.Y)}, {Mm(p.Z)})";

    private static string Mm(double piesInternos) =>
        UnitUtils.ConvertFromInternalUnits(piesInternos, UnitTypeId.Millimeters).ToString("F1", Cultura);
}
