namespace practicacq.Logica;

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

/// <summary>
/// Marca invisible que se graba en cada estribo creado, con el id de la columna
/// que lo originó. Sirve para reconocerlos y borrarlos al regenerar, en vez de
/// ir apilando un juego sobre otro en cada corrida.
/// </summary>
public static class MarcaEstribos
{
    private static readonly Guid IdEsquema = new("9b3e1d24-5a7f-4e08-b6c1-2f8d0a4e91b5");
    private const string NombreEsquema = "PracticaCQEstribos";
    private const string CampoColumna = "IdColumna";

    /// <summary>Debe llamarse dentro de una transacción abierta.</summary>
    public static Schema ObtenerEsquema()
    {
        Schema? existente = Schema.Lookup(IdEsquema);
        if (existente is not null) return existente;

        var constructor = new SchemaBuilder(IdEsquema);
        constructor.SetSchemaName(NombreEsquema);
        constructor.SetReadAccessLevel(AccessLevel.Public);
        constructor.SetWriteAccessLevel(AccessLevel.Public);
        constructor.SetVendorId("ADSK");
        constructor.AddSimpleField(CampoColumna, typeof(ElementId))
                   .SetDocumentation("Id de la columna que originó este estribo.");

        return constructor.Finish();
    }

    public static void Marcar(Element elemento, ElementId idColumna)
    {
        var entidad = new Entity(ObtenerEsquema());
        entidad.Set(CampoColumna, idColumna);
        elemento.SetEntity(entidad);
    }

    /// <summary>Devuelve la columna de origen, o null si el estribo no lo generamos nosotros.</summary>
    public static ElementId? LeerColumna(Element elemento)
    {
        Entity entidad = elemento.GetEntity(ObtenerEsquema());
        return entidad.IsValid() ? entidad.Get<ElementId>(CampoColumna) : null;
    }

    public static ElementFilter Filtro() => new ExtensibleStorageFilter(IdEsquema);
}
