using System.Text.Json.Serialization;

namespace practicacq.Imagenes.Api;

// Forma de las respuestas de la API de CQEING, solo con los campos que usa este add-in. No se usa
// ningún endpoint de escritura.
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
    [JsonPropertyName("codigo_actividad")] public string CodigoActividad { get; set; } = string.Empty;
    [JsonPropertyName("descripcion")] public string Descripcion { get; set; } = string.Empty;
    [JsonPropertyName("unidad")] public string Unidad { get; set; } = string.Empty;
}
