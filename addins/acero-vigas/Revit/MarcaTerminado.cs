using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace CQEING.Vigas.Revit;

/// <summary>
/// La marca de "terminado" que el usuario pone con la palomita junto al nombre del pórtico (punto 5 de Santiago, plan
/// docs/planes/2026-10-28-portico-terminado-vigas.md, decisión 2), guardada en cada viga del pórtico. Mismo mecanismo
/// que <see cref="ConfiguracionPortico"/>, con su propio esquema: un esquema creado no se puede cambiar.
/// </summary>
internal static class MarcaTerminado
{
    private static readonly Guid IdEsquema = new("5d2a9c71-0e4b-4f36-8b1d-7a3e9f60c2b8");
    private const string NombreEsquema = "CQEINGVigasTerminado";
    private const string CampoTerminado = "Terminado";

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
        constructor.AddSimpleField(CampoTerminado, typeof(bool))
            .SetDocumentation("El pórtico de esta viga se marcó como terminado en el tablero de vigas.");
        return constructor.Finish();
    }

    /// <summary>Dentro de una transacción. Sin marca, borra la que hubiera.</summary>
    public static void Marcar(Element viga, bool terminado)
    {
        Schema esquema = Esquema();
        if (!terminado)
        {
            viga.DeleteEntity(esquema);
            return;
        }
        var entidad = new Entity(esquema);
        entidad.Set(CampoTerminado, true);
        viga.SetEntity(entidad);
    }

    /// <summary>Si <paramref name="viga"/> tiene la marca.</summary>
    public static bool Tiene(Element? viga)
    {
        if (viga is null || Schema.Lookup(IdEsquema) is not { } esquema)
            return false;
        Entity entidad = viga.GetEntity(esquema);
        return entidad.IsValid() && entidad.Get<bool>(CampoTerminado);
    }
}
