using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using practicacq.Imagenes.Configuracion;
using practicacq.Imagenes.Modelo;

namespace practicacq.Imagenes.Almacen;

/// <summary>
/// Sube una imagen a Cloudinary con un <i>upload preset</i> sin firma (no hace falta clave secreta).
/// Basado en <c>Services/CloudinaryUploadService.cs</c> del add-in de cantidades (mismos reintentos),
/// más lo que pide la issue #183:
/// - carpeta <c>cuadro_cantidades/imagenes/{presupuesto}/{actividad}/</c>;
/// - <c>public_id</c> = código de la imagen;
/// - <c>tags</c> <c>{presupuesto}</c> y <c>{presupuesto}_{actividad}</c>, para que después el cuadro
///   de cantidades liste las imágenes de una actividad por tag;
/// - <c>context</c> con código, nombre y descripción, más el modelo de Revit del que salió
///   (<c>modelo</c> = id del modelo, <c>nombre_modelo</c>): el vínculo modelo ↔ actividad va en cada
///   imagen, no en el presupuesto.
/// </summary>
public sealed class SubidaCloudinary
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly TimeSpan[] _esperas = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };

    private readonly ConfiguracionImagenes _config;

    public SubidaCloudinary(ConfiguracionImagenes config) => _config = config;

    /// <summary>
    /// Sube el PNG y devuelve la URL. Si ya existe una imagen con ese <c>public_id</c> en Cloudinary,
    /// devuelve <see cref="ResultadoSubida.CodigoOcupado"/> con la URL de la que ya estaba (las subidas
    /// sin firma nunca sobrescriben). Quien llama decide qué significa:
    /// - en una imagen nueva, otra PC ya usó ese código → hay que pasar al siguiente número;
    /// - al reintentar una subida pendiente, es la misma imagen que sí llegó la vez anterior → éxito.
    /// </summary>
    public async Task<ResultadoSubida> SubirAsync(ImagenActividad imagen)
    {
        if (!_config.CloudinaryConfigurado)
            return ResultadoSubida.Fallo($"Falta CLOUDINARY_CLOUD_NAME o CLOUDINARY_UPLOAD_PRESET en {ConfiguracionImagenes.RutaEnv}.");

        byte[] bytes = await File.ReadAllBytesAsync(imagen.RutaLocal);
        string url = $"https://api.cloudinary.com/v1_1/{_config.CloudName}/image/upload";

        for (int intento = 0; ; intento++)
        {
            bool puedeReintentar = intento < _esperas.Length;
            try
            {
                using MultipartFormDataContent formulario = ArmarFormulario(imagen, bytes);
                using HttpResponseMessage respuesta = await _http.PostAsync(url, formulario);
                string cuerpo = await respuesta.Content.ReadAsStringAsync();

                if (respuesta.IsSuccessStatusCode)
                {
                    RespuestaCloudinary? r = JsonSerializer.Deserialize<RespuestaCloudinary>(cuerpo);
                    string urlSubida = r?.UrlSegura ?? r?.Url ?? string.Empty;
                    if (r?.Existente == true) return ResultadoSubida.YaExiste(urlSubida);
                    return ResultadoSubida.Ok(urlSubida);
                }

                bool temporal = respuesta.StatusCode == HttpStatusCode.TooManyRequests || (int)respuesta.StatusCode >= 500;
                if (!temporal || !puedeReintentar)
                    return ResultadoSubida.Fallo($"Cloudinary respondió {(int)respuesta.StatusCode}: {cuerpo}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (!puedeReintentar) return ResultadoSubida.Fallo($"Sin conexión con Cloudinary: {ex.Message}");
            }

            await Task.Delay(_esperas[intento]);
        }
    }

    private MultipartFormDataContent ArmarFormulario(ImagenActividad imagen, byte[] bytes)
    {
        var formulario = new MultipartFormDataContent
        {
            { new StringContent(_config.UploadPreset), "upload_preset" },
            { new StringContent($"cuadro_cantidades/imagenes/{imagen.CodigoPresupuesto}/{imagen.CodigoActividad}"), "folder" },
            { new StringContent(imagen.Codigo), "public_id" },
            { new StringContent($"{imagen.CodigoPresupuesto},{imagen.CodigoPresupuesto}_{imagen.CodigoActividad}"), "tags" },
            {
                new StringContent(
                    $"codigo={ValorContexto(imagen.Codigo)}|nombre={ValorContexto(imagen.Nombre)}|descripcion={ValorContexto(imagen.Descripcion)}" +
                    $"|modelo={ValorContexto(imagen.IdModelo)}|nombre_modelo={ValorContexto(imagen.NombreModelo)}"),
                "context"
            },
        };

        var archivo = new ByteArrayContent(bytes);
        archivo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        formulario.Add(archivo, "file", imagen.Codigo + ".png");
        return formulario;
    }

    /// <summary>En <c>context</c>, <c>|</c> y <c>=</c> separan pares; dentro de un valor van escapados.</summary>
    private static string ValorContexto(string texto) => texto.Replace("|", "\\|").Replace("=", "\\=");

    private sealed class RespuestaCloudinary
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("secure_url")] public string? UrlSegura { get; set; }
        [JsonPropertyName("existing")] public bool Existente { get; set; }
    }
}

public sealed class ResultadoSubida
{
    public bool Exito { get; private init; }
    public bool CodigoOcupado { get; private init; }
    public string Url { get; private init; } = string.Empty;
    public string Error { get; private init; } = string.Empty;

    public static ResultadoSubida Ok(string url) => new() { Exito = true, Url = url };
    public static ResultadoSubida YaExiste(string urlExistente) =>
        new() { CodigoOcupado = true, Url = urlExistente, Error = "Ya existe una imagen con ese código en Cloudinary." };
    public static ResultadoSubida Fallo(string error) => new() { Error = error };
}
