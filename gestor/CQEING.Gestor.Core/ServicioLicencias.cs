using System.Text.Json;

namespace CQEING.Gestor.Core;

public enum EstadoSolicitud { Aprobada, Pendiente, Revocada, SinConexion }

/// <param name="ContenidoLicencia">Solo con Aprobada: el JSON {"datos","firma"} tal como se guarda en licencia.json.</param>
public sealed record RespuestaServicio(EstadoSolicitud Estado, string? ContenidoLicencia = null);

public sealed record DatosSolicitud(string Codigo, string Nombre, string Equipo, string UsuarioWindows, string VersionGestor);

/// <summary>
/// Pide la licencia a la hoja «Licencias CQEING» (tools/licencias/servicio.gs), publicada como aplicación web. Es un GET:
/// Apps Script responde con una redirección que HttpClient sigue solo. Cualquier cosa que no sea una respuesta clara del
/// script (sin red, tiempo agotado, error HTTP, JSON que no se entiende, {"error":…}) es SinConexion. Issue #189.
/// </summary>
public sealed class ServicioLicencias(HttpClient http, string url = ServicioLicencias.Url)
{
    /// <summary>
    /// Implementación de prueba en la cuenta de Juan. Antes de publicar se cambia por la de la cuenta del ingeniero Carlos
    /// (plan docs/planes/2026-10-28-licencias-hoja-google.md, Decisión 10 y Tarea 8).
    /// </summary>
    public const string Url = "https://script.google.com/macros/s/AKfycbxoTN5H3JjdQGcG2nRngX9D-hQBU3kKOjtZhgUxbulFovbHDTivCxFj0YB0WdV4-bLB/exec";

    private static readonly RespuestaServicio SinConexion = new(EstadoSolicitud.SinConexion);

    public async Task<RespuestaServicio> PedirAsync(DatosSolicitud datos, CancellationToken ct = default)
    {
        string consulta = string.Join("&",
            Parametro("codigo", datos.Codigo),
            Parametro("nombre", datos.Nombre),
            Parametro("equipo", datos.Equipo),
            Parametro("usuarioWindows", datos.UsuarioWindows),
            Parametro("version", datos.VersionGestor));

        string json;
        try
        {
            using HttpResponseMessage respuesta = await http.GetAsync($"{url}?{consulta}", ct);
            if (!respuesta.IsSuccessStatusCode) return SinConexion;
            json = await respuesta.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException)
        {
            return SinConexion;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Lo canceló el tiempo máximo del HttpClient, no quien llamó.
            return SinConexion;
        }
        return Interpretar(json);
    }

    private static string Parametro(string nombre, string valor) => $"{nombre}={Uri.EscapeDataString(valor)}";

    private static RespuestaServicio Interpretar(string json)
    {
        try
        {
            using JsonDocument documento = JsonDocument.Parse(json);
            JsonElement raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object
                || !raiz.TryGetProperty("estado", out JsonElement estado)
                || estado.ValueKind != JsonValueKind.String)
                return SinConexion;

            return estado.GetString() switch
            {
                "pendiente" => new RespuestaServicio(EstadoSolicitud.Pendiente),
                "revocado" => new RespuestaServicio(EstadoSolicitud.Revocada),
                "aprobado" when raiz.TryGetProperty("licencia", out JsonElement licencia) && licencia.ValueKind == JsonValueKind.Object
                    => new RespuestaServicio(EstadoSolicitud.Aprobada, licencia.GetRawText()),
                _ => SinConexion,
            };
        }
        catch (JsonException)
        {
            return SinConexion;
        }
    }
}
