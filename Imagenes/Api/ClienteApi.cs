using System.Net.Http;
using System.Text.Json;
using practicacq.Imagenes.Configuracion;
using practicacq.Imagenes.Modelo;

namespace practicacq.Imagenes.Api;

/// <summary>
/// Lee presupuestos y estructura de la API de CQEING con la misma <c>X-Api-Key</c> del add-in de
/// cantidades. Solo lectura: este add-in no escribe nada en la API ni en la web (issue #183).
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
                            Codigo = i.CodigoActividad,
                            Descripcion = i.Descripcion,
                            Unidad = i.Unidad,
                        })
                        .ToList(),
                }).ToList(),
            })
            .ToList();
    }

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
