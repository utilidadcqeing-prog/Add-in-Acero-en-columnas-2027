using System.Text.Json.Serialization;

namespace practicacq.Imagenes.Api;

// Forma de las respuestas de la API de CQEING, solo con los campos que usa este add-in. La única
// escritura es el registro de imágenes por ítem (fase B, ver ImagenItemDto al final).
//
// Ojo, los dos endpoints no usan el mismo estilo de nombres (verificado contra la API de producción
// en Railway el 2026-10-13):
// - GET /api/presupuestos responde en camelCase y paginado:
//   { "total", "page", "pageSize", "pages", "data": [ { "codigoPresupuesto", "nombre", ... } ] }
// - GET /api/presupuestos/{codigo}/estructura responde en snake_case:
//   { "codigo_presupuesto", "capitulos": [ { "codigo_capitulo", "subcapitulos": [ { "items": [ { "codigo_actividad", ... } ] } ] } ] }
// Para el código del presupuesto se aceptan los dos estilos, por si la API cambia.

internal sealed class PresupuestoDto
{
    [JsonPropertyName("codigoPresupuesto")] public string? CodigoCamel { get; set; }
    [JsonPropertyName("codigo_presupuesto")] public string? CodigoSnake { get; set; }
    [JsonPropertyName("nombre")] public string Nombre { get; set; } = string.Empty;

    // El presupuesto trae también revitModelId (vínculo que hace el add-in de cantidades), pero aquí
    // no se usa: el modelo se vincula a cada imagen de una actividad, no al presupuesto.

    // [JsonIgnore]: sin esto choca con "codigoPresupuesto" (la deserialización no distingue mayúsculas).
    [JsonIgnore]
    public string CodigoPresupuesto
    {
        get => CodigoCamel ?? CodigoSnake ?? string.Empty;
        set => CodigoCamel = value;
    }
}

/// <summary>
/// <c>GET /api/presupuestos</c> responde paginado (<c>{ "data": [...], "page", "pages" }</c>); se acepta
/// también una lista suelta, como la manejaba el add-in de cantidades.
/// </summary>
internal sealed class ListaPresupuestosDto
{
    [JsonPropertyName("data")] public List<PresupuestoDto>? Data { get; set; }
    [JsonPropertyName("page")] public int Pagina { get; set; } = 1;
    [JsonPropertyName("pages")] public int Paginas { get; set; } = 1;
}

internal sealed class EstructuraDto
{
    [JsonPropertyName("codigo_presupuesto")] public string CodigoPresupuesto { get; set; } = string.Empty;
    [JsonPropertyName("capitulos")] public List<CapituloDto> Capitulos { get; set; } = new();
}

internal sealed class CapituloDto
{
    [JsonPropertyName("codigo_capitulo")] public string Codigo { get; set; } = string.Empty;
    [JsonPropertyName("nombre")] public string Nombre { get; set; } = string.Empty;
    [JsonPropertyName("subcapitulos")] public List<SubcapituloDto> Subcapitulos { get; set; } = new();
}

internal sealed class SubcapituloDto
{
    [JsonPropertyName("codigo_subcapitulo")] public string Codigo { get; set; } = string.Empty;
    [JsonPropertyName("nombre")] public string Nombre { get; set; } = string.Empty;
    [JsonPropertyName("items")] public List<ItemDto> Items { get; set; } = new();
}

internal sealed class ItemDto
{
    /// <summary>Id del ítem en la BD: las imágenes se registran contra él (fase B, ver plan 2026-10-14-imagenes-por-actividad-en-bd).</summary>
    [JsonPropertyName("id_item")] public int IdItem { get; set; }
    [JsonPropertyName("codigo_actividad")] public string CodigoActividad { get; set; } = string.Empty;
    [JsonPropertyName("descripcion")] public string Descripcion { get; set; } = string.Empty;
    [JsonPropertyName("unidad")] public string Unidad { get; set; } = string.Empty;
}

/// <summary>
/// Una imagen registrada en la BD contra un ítem (fase B, contrato propuesto en
/// <c>docs/planes/2026-10-14-imagenes-por-actividad-en-bd.md</c>): es el cuerpo del POST y cada
/// elemento del GET de <c>/api/presupuestos/items/{idItem}/imagenes</c>. <c>id</c> y
/// <c>fecha_creacion</c> solo vienen en las respuestas.
/// </summary>
internal sealed class ImagenItemDto
{
    [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Id { get; set; }
    [JsonPropertyName("codigo")] public string Codigo { get; set; } = string.Empty;
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("nombre")] public string Nombre { get; set; } = string.Empty;
    [JsonPropertyName("descripcion")] public string Descripcion { get; set; } = string.Empty;
    [JsonPropertyName("tipo_vista")] public string TipoVista { get; set; } = string.Empty;
    [JsonPropertyName("id_modelo")] public string IdModelo { get; set; } = string.Empty;
    [JsonPropertyName("nombre_modelo")] public string NombreModelo { get; set; } = string.Empty;
    [JsonPropertyName("colores")] public List<ColorImagenDto> Colores { get; set; } = new();
    [JsonPropertyName("fecha_creacion"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTime? FechaCreacion { get; set; }
}

internal sealed class ColorImagenDto
{
    [JsonPropertyName("tipo")] public string Tipo { get; set; } = string.Empty;
    [JsonPropertyName("color")] public string Color { get; set; } = string.Empty;
}
