using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Application.Interfaces;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Servicio responsable de subir imágenes a Cloudinary usando el endpoint de carga no firmado (Unsigned Upload).
/// Reintenta los fallos temporales (red, tiempo de espera, 5xx y 429) con espera creciente.
/// </summary>
public sealed class CloudinaryUploadService : IImageUploadService
{
    private const string LogSource = "CloudinaryUploadService";

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Esperas antes de cada reintento: hasta 4 intentos en total.
    /// </summary>
    private static readonly TimeSpan[] _retryDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
    };

    /// <summary>
    /// Nombre de la nube (Cloud Name) de Cloudinary.
    /// </summary>
    public string CloudName { get; set; } = "qkrf4kvz";

    /// <summary>
    /// Preset de subida no firmado (Unsigned Upload Preset) de Cloudinary.
    /// </summary>
    public string UploadPreset { get; set; } = "addin_cuadro_cantidades";

    /// <inheritdoc />
    public async Task<string?> UploadImageAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            ErrorLog.Write(LogSource, "Archivo de imagen inválido o no existe: " + (filePath ?? "nulo"));
            return null;
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
        return await UploadImageAsync(fileBytes, Path.GetFileName(filePath), "image/png");
    }

    /// <inheritdoc />
    public async Task<string?> UploadImageAsync(byte[] imageData, string fileName, string contentType)
    {
        if (imageData == null || imageData.Length == 0)
        {
            ErrorLog.Write(LogSource, $"Imagen vacía: {fileName}");
            return null;
        }

        if (CloudName == "TU_CLOUD_NAME" || UploadPreset == "TU_UPLOAD_PRESET")
        {
            ErrorLog.Write(LogSource, "CloudName o UploadPreset no han sido configurados con credenciales reales.");
            return null;
        }

        string url = $"https://api.cloudinary.com/v1_1/{CloudName}/image/upload";

        for (int attempt = 0; ; attempt++)
        {
            bool canRetry = attempt < _retryDelays.Length;
            try
            {
                using var content = BuildContent(imageData, fileName, contentType);
                using var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<CloudinaryResponse>(responseString);
                    return result?.SecureUrl ?? result?.Url;
                }

                string errorContent = await response.Content.ReadAsStringAsync();
                string error = $"Error en Cloudinary (HTTP {(int)response.StatusCode}) al subir {fileName}: {errorContent}";

                if (!IsTransient(response.StatusCode) || !canRetry)
                {
                    ErrorLog.Write(LogSource, $"{error}. Intento {attempt + 1}, sin más reintentos.");
                    throw new InvalidOperationException(error);
                }

                ErrorLog.Write(LogSource, $"{error}. Intento {attempt + 1}, se reintentará.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Fallo de red o tiempo de espera agotado
                if (!canRetry)
                {
                    ErrorLog.Write(LogSource, $"Fallo al subir {fileName}. Intento {attempt + 1}, sin más reintentos.", ex);
                    throw new InvalidOperationException($"Fallo al subir la imagen a Cloudinary: {ex.Message}", ex);
                }

                ErrorLog.Write(LogSource, $"Fallo al subir {fileName}. Intento {attempt + 1}, se reintentará.", ex);
            }

            await Task.Delay(_retryDelays[attempt]);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private MultipartFormDataContent BuildContent(byte[] imageData, string fileName, string contentType)
    {
        string boundary = "MyCustomBoundary" + DateTime.Now.Ticks.ToString("x");
        var content = new MultipartFormDataContent(boundary);

        // 1. Adjuntar upload_preset con comillas forzadas para compatibilidad con Cloudinary
        content.Add(CreateFormField("upload_preset", UploadPreset));

        // 1.5 Adjuntar folder para la organización de los assets en Cloudinary
        content.Add(CreateFormField("folder", "cuadro_cantidades"));

        // 2. Adjuntar el archivo con comillas forzadas
        var fileContent = new ByteArrayContent(imageData);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        fileContent.Headers.Remove("Content-Disposition");
        fileContent.Headers.TryAddWithoutValidation(
            "Content-Disposition",
            $"form-data; name=\"file\"; filename=\"{fileName}\""
        );
        content.Add(fileContent);

        // 3. Forzar el content type sin comillas en el boundary para evitar fallos de parser en Cloudinary
        content.Headers.Remove("Content-Type");
        content.Headers.TryAddWithoutValidation(
            "Content-Type",
            $"multipart/form-data; boundary={boundary}"
        );

        return content;
    }

    private static StringContent CreateFormField(string name, string value)
    {
        var field = new StringContent(value);
        field.Headers.ContentType = null;
        field.Headers.Remove("Content-Disposition");
        field.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"{name}\"");
        return field;
    }

    private class CloudinaryResponse
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("secure_url")]
        public string? SecureUrl { get; set; }
    }
}
