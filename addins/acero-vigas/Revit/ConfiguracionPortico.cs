using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace CQEING.Vigas.Revit;

/// <summary>
/// La configuración del tablero con que se generó el pórtico, guardada en cada una de sus vigas al generar (plan
/// docs/planes/2026-10-22-recordar-portico-generado-vigas.md, decisiones 1 y 2): el texto de
/// <c>ConfiguracionGuardada</c>. Mismo mecanismo que <see cref="MarcaAcero"/>, con su propio esquema.
/// </summary>
internal static class ConfiguracionPortico
{
    private static readonly Guid IdEsquema = new("b3e7d1a4-6c25-4f98-9a0b-2d84e6c1f573");
    private const string NombreEsquema = "CQEINGVigasTablero";
    private const string CampoConfiguracion = "Configuracion";

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
        constructor.AddSimpleField(CampoConfiguracion, typeof(string))
            .SetDocumentation("Configuración del tablero de vigas con que se generó el acero de este pórtico (JSON).");
        return constructor.Finish();
    }

    /// <summary>Dentro de una transacción. Reemplaza lo que hubiera.</summary>
    public static void Guardar(Element viga, string configuracion)
    {
        var entidad = new Entity(Esquema());
        entidad.Set(CampoConfiguracion, configuracion);
        viga.SetEntity(entidad);
    }

    /// <summary>Lo guardado en <paramref name="viga"/>, o null si no tiene nada.</summary>
    public static string? Leer(Element? viga)
    {
        if (viga is null || Schema.Lookup(IdEsquema) is not { } esquema)
            return null;
        Entity entidad = viga.GetEntity(esquema);
        return entidad.IsValid() ? entidad.Get<string>(CampoConfiguracion) : null;
    }
}
