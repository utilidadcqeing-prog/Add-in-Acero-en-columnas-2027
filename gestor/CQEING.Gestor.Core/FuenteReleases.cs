using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CQEING.Gestor.Core;

public sealed record ArchivoRelease(
    [property: JsonPropertyName("name")] string Nombre,
    [property: JsonPropertyName("browser_download_url")] Uri Url);

public sealed record ReleaseGitHub(
    [property: JsonPropertyName("tag_name")] string Etiqueta,
    [property: JsonPropertyName("draft")] bool Borrador,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("assets")] IReadOnlyList<ArchivoRelease> Archivos);

/// <param name="Aviso">Texto para el usuario cuando se usó la lista guardada en vez de la de GitHub.</param>
public sealed record ResultadoReleases(IReadOnlyList<ReleaseGitHub> Releases, string? Aviso)
{
    /// <summary>La lista viene de la caché (sin conexión o GitHub limitó): solo se puede volver a versiones en disco.</summary>
    public bool DesdeCache => Aviso is not null;
}

/// <summary>
/// Releases del repo público, sin token. GitHub da 60 consultas por hora por IP pública (una oficina
/// comparte el cupo). La respuesta se guarda con su ETag: si no cambió, GitHub responde 304 sin volver a
/// mandar la lista. Ojo: medido el 2026-10-15, sin token el 304 SÍ descuenta una consulta del cupo
/// (docs/planes/2026-10-15-gestor-logica-74.md, Hallazgos). Si GitHub limita, no hay red o responde algo
/// ilegible, se usa la última lista guardada con un aviso; sin lista guardada, lanza GestorException.
/// </summary>
public sealed class FuenteReleases(HttpClient http, string carpetaCache, string repositorio = Repositorio.Nombre)
{
    private const string SeUsaLaGuardada = "Se muestra la última lista guardada.";

    private string RutaJson => Path.Combine(carpetaCache, "releases.json");
    private string RutaEtag => Path.Combine(carpetaCache, "releases.etag");

    public Task<ResultadoReleases> ObtenerAsync(CancellationToken ct = default) => ObtenerAsync(usarEtag: true, ct);

    private async Task<ResultadoReleases> ObtenerAsync(bool usarEtag, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repositorio}/releases?per_page=100");
        pedido.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (usarEtag && File.Exists(RutaJson) && File.Exists(RutaEtag))
            pedido.Headers.TryAddWithoutValidation("If-None-Match", File.ReadAllText(RutaEtag).Trim());

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.SendAsync(pedido, ct);
        }
        catch (HttpRequestException e)
        {
            return DesdeCache(TipoError.SinConexion, "No hay conexión con GitHub.", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            // Lo canceló el tiempo máximo del HttpClient, no el usuario.
            return DesdeCache(TipoError.SinConexion, "GitHub no respondió a tiempo.", e);
        }

        using (respuesta)
        {
            if (respuesta.StatusCode == HttpStatusCode.NotModified)
            {
                if (LeerCache() is { } guardadas) return new ResultadoReleases(guardadas, null);
                // La lista guardada se dañó o se borró: se vuelve a pedir entera, una sola vez.
                return usarEtag
                    ? await ObtenerAsync(usarEtag: false, ct)
                    : throw new GestorException(TipoError.SinConexion, "GitHub respondió «sin cambios» y no hay una lista guardada.");
            }
            if (EsLimite(respuesta))
                return DesdeCache(TipoError.LimiteGitHub, "GitHub limitó las consultas de esta red; se libera en menos de una hora.", null);
            if (!respuesta.IsSuccessStatusCode)
                return DesdeCache(TipoError.SinConexion, $"GitHub respondió con el error {(int)respuesta.StatusCode}.", null);

            string json = await respuesta.Content.ReadAsStringAsync(ct);
            if (Interpretar(json) is not { } releases)
                return DesdeCache(TipoError.SinConexion, "GitHub devolvió una lista de releases que no se pudo leer.", null);

            Guardar(json, respuesta.Headers.ETag?.ToString());
            return new ResultadoReleases(releases, null);
        }
    }

    private static bool EsLimite(HttpResponseMessage respuesta) =>
        respuesta.StatusCode == HttpStatusCode.TooManyRequests
        || (respuesta.StatusCode == HttpStatusCode.Forbidden
            && respuesta.Headers.TryGetValues("x-ratelimit-remaining", out IEnumerable<string>? restantes)
            && restantes.FirstOrDefault() == "0");

    private ResultadoReleases DesdeCache(TipoError tipo, string mensaje, Exception? causa)
    {
        IReadOnlyList<ReleaseGitHub> guardadas = LeerCache() ?? throw new GestorException(tipo, mensaje, causa);
        return new ResultadoReleases(guardadas, $"{mensaje} {SeUsaLaGuardada}");
    }

    private IReadOnlyList<ReleaseGitHub>? LeerCache() => File.Exists(RutaJson) ? Interpretar(File.ReadAllText(RutaJson)) : null;

    /// <summary>Escribe a un temporal y lo mueve: un corte a mitad nunca deja una lista guardada a medias.</summary>
    private void Guardar(string json, string? etag)
    {
        Directory.CreateDirectory(carpetaCache);
        string temporal = RutaJson + ".tmp";
        File.WriteAllText(temporal, json);
        File.Move(temporal, RutaJson, overwrite: true);
        if (etag is not null) File.WriteAllText(RutaEtag, etag);
        else File.Delete(RutaEtag);
    }

    /// <summary>
    /// Lee la lista de GitHub. Las releases sin etiqueta y los archivos sin nombre o sin URL https absoluta se
    /// descartan aquí, para que el resto del gestor no tenga que comprobarlo. Devuelve null si no es una lista.
    /// </summary>
    private static IReadOnlyList<ReleaseGitHub>? Interpretar(string json)
    {
        List<ReleaseGitHub?>? releases;
        try
        {
            releases = JsonSerializer.Deserialize<List<ReleaseGitHub?>>(json);
        }
        catch (JsonException)
        {
            return null;
        }

        return releases?
            .OfType<ReleaseGitHub>()
            .Where(r => !string.IsNullOrWhiteSpace(r.Etiqueta))
            .Select(r => r with { Archivos = (r.Archivos ?? []).Where(ArchivoValido).ToList() })
            .ToList();
    }

    private static bool ArchivoValido(ArchivoRelease? archivo) =>
        archivo is not null
        && !string.IsNullOrWhiteSpace(archivo.Nombre)
        && archivo.Url is { IsAbsoluteUri: true }
        && archivo.Url.Scheme == Uri.UriSchemeHttps;
}
