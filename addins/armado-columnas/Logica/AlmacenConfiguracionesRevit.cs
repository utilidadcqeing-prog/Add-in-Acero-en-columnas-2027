namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using practicacq.Modelo;

/// <summary>
/// Guarda las configuraciones con nombre DENTRO del modelo de Revit, con
/// Extensible Storage (decisión de Luciano, 10-sep: así viajan con el proyecto
/// y las ve cualquiera que lo abra, en vez de quedar en la PC de quien las
/// creó).
///
/// El esquema tiene UN solo campo de texto con el JSON de todas las
/// configuraciones, no un campo por dato. Modelar cada propiedad de
/// <see cref="ConfigTramo"/>/<see cref="ConfigLongitudinal"/> como campo del
/// esquema obligaría a migrar el esquema cada vez que se agrega una opción a la
/// ventana -- y un esquema ya escrito en un modelo del cliente no se puede
/// cambiar sin borrarlo. Con el JSON adentro, agregar un campo es un cambio de
/// código y nada más.
///
/// API usada, toda pública y documentada (regla del CLAUDE.md):
/// <c>SchemaBuilder</c>, <c>Schema.Lookup</c>, <c>Entity</c>,
/// <c>Element.GetEntity</c>/<c>SetEntity</c> y <c>DataStorage.Create</c>.
///
/// <b>Escritura y transacción</b>: la ventana es no modal, así que no puede
/// escribir en el documento cuando se le da la gana. <see cref="Guardar"/> y
/// <see cref="Borrar"/> tocan la lista en memoria y avisan por
/// <see cref="CambiosPendientes"/>; quien abre la ventana
/// (<c>ComandoEstribos</c>) engancha eso a un <c>ExternalEvent</c> que llama a
/// <see cref="Persistir"/> dentro de una transacción. Leer no necesita nada de
/// eso.
/// </summary>
public sealed class AlmacenConfiguracionesRevit : IAlmacenConfiguraciones
{
    /// <summary>
    /// Fijo y para siempre: es la identidad del esquema dentro de los modelos
    /// del cliente. Cambiarlo deja huérfanas las configuraciones ya guardadas.
    /// </summary>
    private static readonly Guid IdEsquema = new("6B3C1E24-9A7F-4D18-A5C3-2E7F0B4D91AA");

    private const string NombreEsquema = "CQEING_Configuraciones";
    private const string NombreCampo = "Json";

    private readonly Document _doc;
    private readonly List<ConfiguracionGuardada> _guardadas;

    /// <summary>Se dispara cuando hay algo nuevo que escribir en el documento.</summary>
    public event Action? CambiosPendientes;

    public AlmacenConfiguracionesRevit(Document doc)
    {
        _doc = doc;
        _guardadas = LeerDelDocumento();
    }

    public IReadOnlyList<ConfiguracionGuardada> Listar() => _guardadas;

    public void Guardar(ConfiguracionGuardada configuracion)
    {
        _guardadas.RemoveAll(g => g.Nombre == configuracion.Nombre);
        _guardadas.Add(configuracion);
        CambiosPendientes?.Invoke();
    }

    public void Borrar(string nombre)
    {
        if (_guardadas.RemoveAll(g => g.Nombre == nombre) > 0) CambiosPendientes?.Invoke();
    }

    /// <summary>
    /// Vuelca la lista en memoria al documento. Tiene que llamarse DENTRO de
    /// una transacción abierta por quien corresponda (ver el resumen de la
    /// clase). Devuelve el motivo si no se pudo, o <c>null</c> si salió bien --
    /// no tira excepción: que falle guardar una configuración no puede tumbar
    /// la ventana.
    /// </summary>
    public string? Persistir()
    {
        try
        {
            Schema esquema = ObtenerOCrearEsquema();

            Element contenedor = BuscarContenedor(_doc, esquema) ?? DataStorage.Create(_doc);
            contenedor.Name = "CQEING - configuraciones";

            var entidad = new Entity(esquema);
            entidad.Set(esquema.GetField(NombreCampo), JsonSerializer.Serialize(_guardadas));
            contenedor.SetEntity(entidad);

            return null;
        }
        catch (Exception ex)
        {
            return GeometriaSeccion.Explicar(ex);
        }
    }

    private List<ConfiguracionGuardada> LeerDelDocumento()
    {
        try
        {
            Schema? esquema = Schema.Lookup(IdEsquema);
            if (esquema is null) return new List<ConfiguracionGuardada>();

            Element? contenedor = BuscarContenedor(_doc, esquema);
            if (contenedor is null) return new List<ConfiguracionGuardada>();

            string json = contenedor.GetEntity(esquema).Get<string>(esquema.GetField(NombreCampo));
            if (string.IsNullOrWhiteSpace(json)) return new List<ConfiguracionGuardada>();

            return JsonSerializer.Deserialize<List<ConfiguracionGuardada>>(json) ?? new List<ConfiguracionGuardada>();
        }
        catch
        {
            // Un modelo con datos de una versión anterior del formato no puede
            // impedir que la ventana abra: se arranca sin configuraciones.
            return new List<ConfiguracionGuardada>();
        }
    }

    /// <summary>
    /// El <see cref="DataStorage"/> que ya lleva una entidad de este esquema, o
    /// <c>null</c> si el modelo todavía no tiene ninguno.
    /// </summary>
    private static Element? BuscarContenedor(Document doc, Schema esquema) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(DataStorage))
            .Cast<Element>()
            .FirstOrDefault(e => e.GetEntity(esquema).IsValid());

    private static Schema ObtenerOCrearEsquema()
    {
        // Lookup es por sesión de Revit, no por documento: si otro documento ya
        // lo creó en esta sesión, se reusa ese mismo (crear uno con el mismo
        // GUID tira excepción).
        if (Schema.Lookup(IdEsquema) is { } existente) return existente;

        var constructor = new SchemaBuilder(IdEsquema);
        constructor.SetSchemaName(NombreEsquema);
        constructor.SetReadAccessLevel(AccessLevel.Public);
        constructor.SetWriteAccessLevel(AccessLevel.Public);
        constructor.AddSimpleField(NombreCampo, typeof(string));

        return constructor.Finish();
    }
}
