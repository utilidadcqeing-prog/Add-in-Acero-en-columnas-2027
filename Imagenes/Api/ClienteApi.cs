using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using practicacq.Imagenes.Configuracion;
using practicacq.Imagenes.Modelo;

namespace practicacq.Imagenes.Api;

/// <summary>
/// Habla con la API de CQEING con la misma <c>X-Api-Key</c> del add-in de cantidades: lee presupuestos
/// y estructura, y registra las imágenes de cada actividad en la BD contra el <c>id_item</c> (fase B,
/// <c>docs/planes/2026-10-14-imagenes-por-actividad-en-bd.md</c>). No toca la interfaz web.
///
/// Sin <c>API_URL</c>/<c>API_KEY</c> en el <c>.env</c> trabaja con <see cref="DatosDeEjemplo"/>, para
/// poder probar el flujo completo en Revit antes de tener la API a mano.
/// </summary>
public sealed class ClienteApi
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private readonly ConfiguracionImagenes _config;

    public ClienteApi(ConfiguracionImagenes config) => _config = config;

    public bool ModoEjemplo => !_config.ApiConfigurada;

    public async Task<List<PresupuestoResumen>> ObtenerPresupuestosAsync()
    {
        List<PresupuestoDto> dtos;
        if (ModoEjemplo)
        {
            dtos = DatosDeEjemplo.Presupuestos();
        }
        else
        {
            dtos = new();
            // Paginado: se piden todas las páginas (con 50 por página hoy cabe todo en una).
            for (int pagina = 1, paginas = 1; pagina <= paginas && pagina <= 50; pagina++)
            {
                string cuerpo = await GetAsync($"/api/presupuestos?page={pagina}&pageSize=50");
                if (cuerpo.TrimStart().StartsWith('['))
                {
                    dtos.AddRange(JsonSerializer.Deserialize<List<PresupuestoDto>>(cuerpo, _json) ?? new());
                    break;
                }

                ListaPresupuestosDto? lista = JsonSerializer.Deserialize<ListaPresupuestosDto>(cuerpo, _json);
                dtos.AddRange(lista?.Data ?? new());
                paginas = Math.Max(1, lista?.Paginas ?? 1);
            }
        }

        return dtos
            .Where(d => !string.IsNullOrWhiteSpace(d.CodigoPresupuesto))
            .GroupBy(d => d.CodigoPresupuesto)
            .Select(g => g.First())
            .Select(d => new PresupuestoResumen { Codigo = d.CodigoPresupuesto, Nombre = d.Nombre })
            .OrderBy(p => p.Codigo, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<Capitulo>> ObtenerEstructuraAsync(string codigoPresupuesto)
    {
        EstructuraDto? dto = ModoEjemplo
            ? DatosDeEjemplo.Estructura(codigoPresupuesto)
            : JsonSerializer.Deserialize<EstructuraDto>(
                await GetAsync($"/api/presupuestos/{Uri.EscapeDataString(codigoPresupuesto)}/estructura"), _json);

        return (dto?.Capitulos ?? new())
            .Select(c => new Capitulo
            {
                Codigo = c.Codigo,
                Nombre = c.Nombre,
                Subcapitulos = c.Subcapitulos.Select(s => new Subcapitulo
                {
                    Codigo = s.Codigo,
                    Nombre = s.Nombre,
                    Actividades = s.Items
                        .Where(i => !string.IsNullOrWhiteSpace(i.CodigoActividad))
                        .Select(i => new ActividadPresupuesto
                        {
                            IdItem = i.IdItem,
                            Codigo = i.CodigoActividad,
                            Descripcion = i.Descripcion,
                            Unidad = i.Unidad,
                        })
                        .ToList(),
                }).ToList(),
            })
            .ToList();
    }

    // ───────────── Imágenes por ítem (fase B) ─────────────

    private static string RutaImagenes(int idItem) => $"/api/presupuestos/items/{idItem}/imagenes";

    /// <summary>
    /// Imágenes registradas para un ítem (de todas las PCs), como <see cref="ImagenActividad"/> sin datos
    /// locales. En modo ejemplo, lista vacía. Lanza excepción si la API falla.
    /// </summary>
    public async Task<List<ImagenActividad>> ListarImagenesAsync(int idItem, string codigoActividad, string codigoPresupuesto)
    {
        if (ModoEjemplo || idItem <= 0) return new();

        var (estado, cuerpo) = await EnviarAsync(HttpMethod.Get, RutaImagenes(idItem), null);
        if (estado != HttpStatusCode.OK)
            throw new InvalidOperationException($"La API respondió {(int)estado} al listar las imágenes del ítem {idItem}.");

        return (JsonSerializer.Deserialize<List<ImagenItemDto>>(cuerpo, _json) ?? new())
            .Select(d => new ImagenActividad
            {
                Codigo = d.Codigo,
                CodigoActividad = codigoActividad,
                CodigoPresupuesto = codigoPresupuesto,
                Nombre = d.Nombre,
                Descripcion = d.Descripcion,
                TipoVista = d.TipoVista,
                IdModelo = d.IdModelo,
                NombreModelo = d.NombreModelo,
                Colores = d.Colores.Select(c => new ColorDeTipo { NombreTipo = c.Tipo, ColorHex = c.Color }).ToList(),
                Url = d.Url,
                Fecha = d.FechaCreacion ?? DateTime.MinValue,
                IdItem = idItem,
                IdApi = d.Id ?? 0,
                RegistradaEnApi = true,
                SoloEnApi = true,
            })
            .ToList();
    }

    /// <summary>
    /// Registra la imagen (POST) y deja su <see cref="ImagenActividad.IdApi"/>. Un 409 ("ese código ya
    /// existe en el ítem") cuenta como registrada: permite reintentar y migrar sin duplicados; el id se
    /// busca con el GET. Devuelve null si salió bien, o el error en texto.
    /// </summary>
    public async Task<string?> RegistrarImagenAsync(ImagenActividad imagen)
    {
        if (ModoEjemplo) return null;
        if (imagen.IdItem <= 0) return "la actividad no tiene id_item en la API.";
        if (string.IsNullOrEmpty(imagen.Url)) return "la imagen todavía no está en Cloudinary.";

        try
        {
            string json = JsonSerializer.Serialize(new ImagenItemDto
            {
                Codigo = imagen.Codigo,
                Url = imagen.Url,
                Nombre = imagen.Nombre,
                Descripcion = imagen.Descripcion,
                TipoVista = imagen.TipoVista,
                IdModelo = imagen.IdModelo,
                NombreModelo = imagen.NombreModelo,
                Colores = imagen.Colores.Select(c => new ColorImagenDto { Tipo = c.NombreTipo, Color = c.ColorHex }).ToList(),
            });
            var (estado, cuerpo) = await EnviarAsync(HttpMethod.Post, RutaImagenes(imagen.IdItem), json);

            if (estado is HttpStatusCode.Created or HttpStatusCode.OK)
            {
                imagen.IdApi = JsonSerializer.Deserialize<ImagenItemDto>(cuerpo, _json)?.Id ?? 0;
                return null;
            }
            if (estado == HttpStatusCode.Conflict)
            {
                imagen.IdApi = await BuscarIdAsync(imagen.IdItem, imagen.Codigo);
                return null;
            }
            return $"la API respondió {(int)estado}: {Recortar(cuerpo)}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Deja el registro de la BD apuntando a la versión actual de la imagen (#186: la URL cambia al
    /// actualizar). El contrato no tiene PUT, así que se borra el registro viejo y se crea de nuevo
    /// (decisión 2 del plan). Devuelve null si salió bien, o el error en texto.
    /// </summary>
    public async Task<string?> ActualizarImagenAsync(ImagenActividad imagen)
    {
        if (ModoEjemplo) return null;
        try
        {
            int id = imagen.IdApi > 0 ? imagen.IdApi : await BuscarIdAsync(imagen.IdItem, imagen.Codigo);
            if (id > 0)
            {
                var (estado, cuerpo) = await EnviarAsync(HttpMethod.Delete, $"{RutaImagenes(imagen.IdItem)}/{id}", null);
                if (estado is not (HttpStatusCode.OK or HttpStatusCode.NoContent or HttpStatusCode.NotFound))
                    return $"la API respondió {(int)estado} al borrar el registro anterior: {Recortar(cuerpo)}";
                imagen.IdApi = 0;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            return ex.Message;
        }
        return await RegistrarImagenAsync(imagen);
    }

    private async Task<int> BuscarIdAsync(int idItem, string codigo)
    {
        var (estado, cuerpo) = await EnviarAsync(HttpMethod.Get, RutaImagenes(idItem), null);
        if (estado != HttpStatusCode.OK) return 0;
        return (JsonSerializer.Deserialize<List<ImagenItemDto>>(cuerpo, _json) ?? new())
            .FirstOrDefault(d => string.Equals(d.Codigo, codigo, StringComparison.OrdinalIgnoreCase))?.Id ?? 0;
    }

    /// <summary>
    /// Llamada a la API que exige JSON en las respuestas con cuerpo. Las rutas que no existen responden
    /// 200 con el HTML de la web (hallazgo 3 del plan de la fase B): eso es "endpoint no disponible", nunca
    /// un éxito.
    /// </summary>
    private async Task<(HttpStatusCode estado, string cuerpo)> EnviarAsync(HttpMethod metodo, string ruta, string? json)
    {
        using var pedido = new HttpRequestMessage(metodo, _config.ApiUrl + ruta);
        pedido.Headers.Add("X-Api-Key", _config.ApiKey);
        if (json is not null) pedido.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using HttpResponseMessage respuesta = await _http.SendAsync(pedido);
        string cuerpo = await respuesta.Content.ReadAsStringAsync();
        string tipo = respuesta.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (cuerpo.Length > 0 && !tipo.Contains("json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"la API todavía no tiene {ruta} (respondió {tipo}, no JSON).");
        return (respuesta.StatusCode, cuerpo);
    }

    private static string Recortar(string texto) => texto.Length <= 200 ? texto : texto[..200] + "…";

    private async Task<string> GetAsync(string ruta)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, _config.ApiUrl + ruta);
        pedido.Headers.Add("X-Api-Key", _config.ApiKey);

        using HttpResponseMessage respuesta = await _http.SendAsync(pedido);
        string cuerpo = await respuesta.Content.ReadAsStringAsync();
        if (!respuesta.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"La API respondió {(int)respuesta.StatusCode} en {ruta}. Revisa API_URL y API_KEY en {ConfiguracionImagenes.RutaEnv}.");
        }
        return cuerpo;
    }
}
