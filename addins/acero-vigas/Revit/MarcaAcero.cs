using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;

namespace CQEING.Vigas.Revit;

/// <summary>
/// La marca invisible que lleva cada barra que crea el add-in de vigas (#85, decisión 6 del plan
/// docs/planes/2026-10-19-generar-acero-vigas-85.md): el pórtico del tablero y la pieza (<c>S1</c>, <c>BS2</c>, <c>E</c>).
/// Sirve para volver a generar sin duplicar y sin tocar lo que se modeló a mano. Mismo mecanismo que
/// <c>MarcaEstribos</c> del add-in de columnas, con su propio esquema.
/// </summary>
internal static class MarcaAcero
{
    private static readonly Guid IdEsquema = new("4f6c2a91-8d3e-4b57-a0e2-7c19d5b8f346");
    private const string NombreEsquema = "CQEINGVigasAcero";
    private const string CampoPortico = "Portico";
    private const string CampoPieza = "Pieza";

    /// <summary>Debe llamarse dentro de una transacción abierta la primera vez (crea el esquema).</summary>
    private static Schema Esquema()
    {
        if (Schema.Lookup(IdEsquema) is { } existente)
            return existente;
        var constructor = new SchemaBuilder(IdEsquema);
        constructor.SetSchemaName(NombreEsquema);
        constructor.SetReadAccessLevel(AccessLevel.Public);
        constructor.SetWriteAccessLevel(AccessLevel.Vendor);
        constructor.SetVendorId("CQEING");
        constructor.AddSimpleField(CampoPortico, typeof(string)).SetDocumentation("Pórtico del tablero de vigas que creó esta barra.");
        constructor.AddSimpleField(CampoPieza, typeof(string)).SetDocumentation("Id de la pieza en el tablero (S1, BI2, E…).");
        return constructor.Finish();
    }

    public static void Marcar(Element e, string portico, string pieza)
    {
        var entidad = new Entity(Esquema());
        entidad.Set(CampoPortico, portico);
        entidad.Set(CampoPieza, pieza);
        e.SetEntity(entidad);
    }

    /// <summary>El pórtico que creó <paramref name="e"/>, o null si no lo creó el add-in.</summary>
    public static string? Portico(Element e)
    {
        if (Schema.Lookup(IdEsquema) is not { } esquema)
            return null;
        Entity entidad = e.GetEntity(esquema);
        return entidad.IsValid() ? entidad.Get<string>(CampoPortico) : null;
    }

    /// <summary>
    /// Borra las barras marcadas que hospeda una viga del pórtico o que dicen ser del pórtico <paramref name="portico"/>
    /// (por si su viga cambió). Nunca toca las que no tienen la marca. Devuelve cuántas borró.
    /// </summary>
    public static int BorrarAnteriores(Document doc, string portico, IEnumerable<string> vigas)
    {
        if (Schema.Lookup(IdEsquema) is null)
            return 0;
        HashSet<long> hosts = vigas.Select(v => long.TryParse(v, out long id) ? id : -1).ToHashSet();
        List<ElementId> viejas = new FilteredElementCollector(doc).OfClass(typeof(Rebar))
            .WherePasses(new ExtensibleStorageFilter(IdEsquema)).Cast<Rebar>()
            .Where(r => hosts.Contains(r.GetHostId().Value) || Portico(r) == portico)
            .Select(r => r.Id).ToList();
        if (viejas.Count > 0)
            doc.Delete(viejas);
        return viejas.Count;
    }
}
