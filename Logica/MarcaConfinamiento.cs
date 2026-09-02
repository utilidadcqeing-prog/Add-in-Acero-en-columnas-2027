namespace practicacq.Logica;

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

/// <summary>
/// Marca invisible que se graba en cada columneta o vigueta creada, con el id
/// del muro que la originó. Sirve para reconocerlas y borrarlas al regenerar,
/// sin tocar campos visibles como Comentarios.
/// </summary>
public static class MarcaConfinamiento
{
    private static readonly Guid IdEsquema = new("7c4f2a10-3e8b-4c19-9d52-6a1f8b0c4e37");
    private const string NombreEsquema = "PracticaCQConfinamiento";
    private const string CampoMuro = "IdMuro";

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
        constructor.AddSimpleField(CampoMuro, typeof(ElementId))
                   .SetDocumentation("Id del muro que originó este elemento.");

        return constructor.Finish();
    }

    public static void Marcar(Element elemento, ElementId idMuro)
    {
        var entidad = new Entity(ObtenerEsquema());
        entidad.Set(CampoMuro, idMuro);
        elemento.SetEntity(entidad);
    }

    /// <summary>Devuelve el muro de origen, o null si el elemento no lo generamos nosotros.</summary>
    public static ElementId? LeerMuro(Element elemento)
    {
        Entity entidad = elemento.GetEntity(ObtenerEsquema());
        return entidad.IsValid() ? entidad.Get<ElementId>(CampoMuro) : null;
    }

    public static ElementFilter Filtro() => new ExtensibleStorageFilter(IdEsquema);
}
