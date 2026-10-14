using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using practicacq.Imagenes.Configuracion;
using practicacq.Imagenes.Modelo;

namespace practicacq.Imagenes.Almacen;

/// <summary>
/// Sube y borra imágenes en Cloudinary. Basado en <c>Services/CloudinaryUploadService.cs</c> del
/// add-in de cantidades (mismos reintentos), más lo que piden las issues #183 y #186:
/// - carpeta <c>cuadro_cantidades/imagenes/{presupuesto}/{actividad}/</c>;
/// - <c>public_id</c> = código de la imagen, con sufijo de versión al actualizarla
///   (<see cref="ImagenActividad.PublicIdDeVersion"/>);
/// - <c>tags</c> <c>{presupuesto}</c> y <c>{presupuesto}_{actividad}</c>, para que después el cuadro
///   de cantidades liste las imágenes de una actividad por tag;
/// - <c>context</c> con código, nombre y descripción, más el modelo de Revit del que salió
///   (<c>modelo</c> = id del modelo, <c>nombre_modelo</c>): el vínculo modelo ↔ actividad va en cada
///   imagen, no en el presupuesto.
///
/// Con API Key + Secret (decisión 10 de <c>docs/planes/2026-10-14-actualizar-imagenes.md</c>) se
/// sube con firma: el <c>public_id</c> lleva la carpeta completa, así que el mismo código en dos
/// presupuestos no choca, y se pueden borrar las versiones anteriores. Sin ellas se usa el preset sin
/// firma, como antes, y no se puede borrar.
/// </summary>
public sealed class SubidaCloudinary
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly TimeSpan[] _esperas = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };

    private readonly ConfiguracionImagenes _config;

    public SubidaCloudinary(ConfiguracionImagenes config) => _config = config;

    public bool PuedeBorrar => _config.CloudinaryFirmado;

    /// <summary>
    /// Sube el PNG de la versión actual de la imagen y devuelve la URL y el <c>public_id</c>. Si ya
    /// existe una imagen con ese <c>public_id</c> en Cloudinary, devuelve
    /// <see cref="ResultadoSubida.CodigoOcupado"/> con la URL de la que ya estaba (nunca se
    /// sobrescribe). Quien llama decide qué significa:
    /// - en una imagen nueva, otra PC ya usó ese código → hay que pasar al siguiente número;
    /// - al reintentar una subida pendiente, es la misma imagen que sí llegó la vez anterior → éxito.
    /// </summary>
    public async Task<ResultadoSubida> SubirAsync(ImagenActividad imagen)
    {
        if (!_config.CloudinaryConfigurado)
            return ResultadoSubida.Fallo(
                $"Falta CLOUDINARY_CLOUD_NAME y CLOUDINARY_API_KEY + CLOUDINARY_API_SECRET (o CLOUDINARY_UPLOAD_PRESET) en {ConfiguracionImagenes.RutaEnv}.");

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
                    string publicId = r?.PublicId ?? string.Empty;
                    if (r?.Existente == true) return ResultadoSubida.YaExiste(urlSubida, publicId);
                    return ResultadoSubida.Ok(urlSubida, publicId);
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

    /// <summary>
    /// Borra una versión anterior (<c>image/destroy</c>, firmado). "not found" también cuenta como
    /// borrada: no hay nada que quitar. Devuelve null si salió bien, o el error en texto.
    /// </summary>
    public async Task<string?> BorrarAsync(string publicId)
    {
        if (!PuedeBorrar)
            return $"Falta CLOUDINARY_API_KEY o CLOUDINARY_API_SECRET en {ConfiguracionImagenes.RutaEnv}.";

        var parametros = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["invalidate"] = "true",
            ["public_id"] = publicId,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
        };

        try
        {
            using var formulario = new MultipartFormDataContent();
            foreach (var (clave, valor) in Firmar(parametros)) formulario.Add(new StringContent(valor), clave);

            using HttpResponseMessage respuesta = await _http.PostAsync(
                $"https://api.cloudinary.com/v1_1/{_config.CloudName}/image/destroy", formulario);
            string cuerpo = await respuesta.Content.ReadAsStringAsync();
            if (!respuesta.IsSuccessStatusCode) return $"Cloudinary respondió {(int)respuesta.StatusCode}: {cuerpo}";

            string? resultado = JsonSerializer.Deserialize<RespuestaBorrado>(cuerpo)?.Resultado;
            return resultado is "ok" or "not found" ? null : $"Cloudinary respondió: {cuerpo}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return $"Sin conexión con Cloudinary: {ex.Message}";
        }
    }

    /// <summary>
    /// Saca el <c>public_id</c> de una URL de Cloudinary, para borrar imágenes subidas antes de que se
    /// guardara el <c>public_id</c>: <c>…/image/upload/v123/carpeta/ACT_006_01.png</c> →
    /// <c>carpeta/ACT_006_01</c>. Devuelve vacío si la URL no tiene esa forma.
    /// </summary>
    public static string PublicIdDesdeUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        int i = url.IndexOf("/upload/", StringComparison.Ordinal);
        if (i < 0) return string.Empty;

        string resto = url[(i + "/upload/".Length)..];
        int barra = resto.IndexOf('/');
        if (barra > 1 && resto[0] == 'v' && resto[1..barra].All(char.IsDigit)) resto = resto[(barra + 1)..];

        int punto = resto.LastIndexOf('.');
        return punto > 0 ? resto[..punto] : resto;
    }

    private MultipartFormDataContent ArmarFormulario(ImagenActividad imagen, byte[] bytes)
    {
        string carpeta = $"cuadro_cantidades/imagenes/{imagen.CodigoPresupuesto}/{imagen.CodigoActividad}";
        string publicId = imagen.PublicIdDeVersion(imagen.Version);
        string tags = $"{imagen.CodigoPresupuesto},{imagen.CodigoPresupuesto}_{imagen.CodigoActividad}";
        string contexto =
            $"codigo={ValorContexto(imagen.Codigo)}|nombre={ValorContexto(imagen.Nombre)}|descripcion={ValorContexto(imagen.Descripcion)}" +
            $"|modelo={ValorContexto(imagen.IdModelo)}|nombre_modelo={ValorContexto(imagen.NombreModelo)}" +
            $"|version={imagen.Version}";

        var formulario = new MultipartFormDataContent();
        if (_config.CloudinaryFirmado)
        {
            // Firmado: el public_id lleva la carpeta (no choca entre presupuestos) y asset_folder la
            // muestra en la consola. overwrite=false para conservar la detección de "código ocupado".
            var parametros = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["asset_folder"] = carpeta,
                ["context"] = contexto,
                ["overwrite"] = "false",
                ["public_id"] = $"{carpeta}/{publicId}",
                ["tags"] = tags,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            };
            foreach (var (clave, valor) in Firmar(parametros)) formulario.Add(new StringContent(valor), clave);
        }
        else
        {
            formulario.Add(new StringContent(_config.UploadPreset), "upload_preset");
            formulario.Add(new StringContent(carpeta), "folder");
            formulario.Add(new StringContent(publicId), "public_id");
            formulario.Add(new StringContent(tags), "tags");
            formulario.Add(new StringContent(contexto), "context");
        }

        var archivo = new ByteArrayContent(bytes);
        archivo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        formulario.Add(archivo, "file", publicId + ".png");
        return formulario;
    }

    /// <summary>
    /// Firma de Cloudinary: <c>SHA-1("a=1&amp;b=2…" + api_secret)</c> con los parámetros en orden
    /// alfabético y sin codificar. Devuelve los parámetros más <c>api_key</c> y <c>signature</c>.
    /// </summary>
    private IEnumerable<(string clave, string valor)> Firmar(SortedDictionary<string, string> parametros)
    {
        string texto = string.Join("&", parametros.Select(p => $"{p.Key}={p.Value}")) + _config.CloudinaryApiSecret;
        string firma = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(texto))).ToLowerInvariant();

        foreach (var p in parametros) yield return (p.Key, p.Value);
        yield return ("api_key", _config.CloudinaryApiKey);
        yield return ("signature", firma);
    }

    /// <summary>En <c>context</c>, <c>|</c> y <c>=</c> separan pares; dentro de un valor van escapados.</summary>
    private static string ValorContexto(string texto) => texto.Replace("|", "\\|").Replace("=", "\\=");

    private sealed class RespuestaCloudinary
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("secure_url")] public string? UrlSegura { get; set; }
        [JsonPropertyName("public_id")] public string? PublicId { get; set; }
        [JsonPropertyName("existing")] public bool Existente { get; set; }
    }

    private sealed class RespuestaBorrado
    {
        [JsonPropertyName("result")] public string? Resultado { get; set; }
    }
}

public sealed class ResultadoSubida
{
    public bool Exito { get; private init; }
    public bool CodigoOcupado { get; private init; }
    public string Url { get; private init; } = string.Empty;
    public string PublicId { get; private init; } = string.Empty;
    public string Error { get; private init; } = string.Empty;

    public static ResultadoSubida Ok(string url, string publicId) => new() { Exito = true, Url = url, PublicId = publicId };
    public static ResultadoSubida YaExiste(string urlExistente, string publicId) =>
        new() { CodigoOcupado = true, Url = urlExistente, PublicId = publicId, Error = "Ya existe una imagen con ese código en Cloudinary." };
    public static ResultadoSubida Fallo(string error) => new() { Error = error };
}
