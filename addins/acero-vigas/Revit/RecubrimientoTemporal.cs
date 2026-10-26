using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace CQEING.Vigas.Revit;

/// <summary>
/// Mientras se crean las grapas, las vigas que las llevan tienen un recubrimiento de Revit casi nulo, y después vuelven al
/// suyo. Medido el 26-oct en la vigueta 389858 (10 cm, recubrimiento de Revit 25): el doblez de la grapa #3 mide 57 mm de
/// ancho y no cabe entre los recubrimientos de los lados; Revit amarra las puntas al recubrimiento y corre la grapa (3.5 mm
/// en C) o la desarma (en S el doblez quedaba a 18 mm de la cara). Creada así, la grapa queda donde la pone el add-in, y al
/// devolver el recubrimiento no se mueve. El tipo de recubrimiento temporal se borra al final: el modelo queda como estaba.
/// </summary>
internal sealed class RecubrimientoTemporal(Document doc)
{
    private const double DistanciaMm = 5;

    private static readonly BuiltInParameter[] Caras =
        [BuiltInParameter.CLEAR_COVER_TOP, BuiltInParameter.CLEAR_COVER_BOTTOM, BuiltInParameter.CLEAR_COVER_OTHER];

    private readonly List<(Parameter Parametro, ElementId Original)> puestos = [];
    private RebarCoverType? tipo;

    public void Poner(IEnumerable<Element> vigas)
    {
        tipo ??= RebarCoverType.Create(doc, NombreLibre(), CreadorBarras.Pies(DistanciaMm));
        foreach (Element viga in vigas)
            foreach (BuiltInParameter cara in Caras)
                if (viga.get_Parameter(cara) is { IsReadOnly: false } p && p.AsElementId() != tipo.Id)
                {
                    puestos.Add((p, p.AsElementId()));
                    p.Set(tipo.Id);
                }
    }

    /// <summary>Devuelve a cada viga su recubrimiento.</summary>
    public void Quitar()
    {
        foreach ((Parameter p, ElementId original) in puestos)
            p.Set(original);
        puestos.Clear();
    }

    /// <summary>Devuelve los recubrimientos y borra el tipo temporal.</summary>
    public void Terminar()
    {
        Quitar();
        if (tipo is not null)
            doc.Delete(tipo.Id);
        tipo = null;
    }

    private string NombreLibre()
    {
        var usados = new FilteredElementCollector(doc).OfClass(typeof(RebarCoverType)).Select(e => e.Name).ToHashSet();
        string nombre = "CQEING temporal";
        for (int i = 2; usados.Contains(nombre); i++)
            nombre = $"CQEING temporal {i}";
        return nombre;
    }
}
