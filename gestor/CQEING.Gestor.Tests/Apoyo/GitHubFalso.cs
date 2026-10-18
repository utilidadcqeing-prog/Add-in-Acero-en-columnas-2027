using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CQEING.Gestor.Core;

namespace CQEING.Gestor.Tests.Apoyo;

/// <summary>
/// GitHub simulado: releases con manifest.json y ZIP válidos (SHA-256 real), sin red. Cuenta las consultas a la
/// lista de releases y puede frenar las descargas para ver la ventana «a mitad» de una operación.
/// </summary>
internal sealed class GitHubFalso : HttpMessageHandler
{
    public const string Repo = "prueba/repo";
    private const string UrlReleases = $"https://api.github.com/repos/{Repo}/releases?per_page=100";

    private readonly Dictionary<string, byte[]> _archivos = [];
    private readonly List<object> _releases = [];

    public bool SinRed { get; set; }
    public int ConsultasDeReleases { get; private set; }

    /// <summary>Si tiene valor, cada descarga de un ZIP espera a que se complete.</summary>
    public TaskCompletionSource? Freno { get; set; }

    public void PublicarGestor(string version) =>
        _releases.Add(new { tag_name = $"gestor-v{version}", draft = false, prerelease = false, assets = Array.Empty<object>() });

    public void Publicar(string id, string nombre, string version, string notas = "Notas de prueba.", params string[] anios)
    {
        var assets = new List<object>();
        var paquetes = new List<PaqueteManifiesto>();
        foreach (string anio in anios.Length == 0 ? ["2026"] : anios)
        {
            byte[] zip = CrearZip(id);
            string archivo = $"{id}-{version}-revit{anio}.zip";
            string url = $"https://github.test/{id}-v{version}/{archivo}";
            _archivos[url] = zip;
            assets.Add(new { name = archivo, browser_download_url = url });
            paquetes.Add(new PaqueteManifiesto(anio, "net8.0-windows", archivo, Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant()));
        }

        var manifiesto = new Manifiesto(1, id, nombre, $"Descripción de {nombre}.", version,
            new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), notas, [], paquetes);
        string urlManifiesto = $"https://github.test/{id}-v{version}/manifest.json";
        _archivos[urlManifiesto] = Encoding.UTF8.GetBytes(SerializadorManifiesto.Escribir(manifiesto));
        assets.Add(new { name = "manifest.json", browser_download_url = urlManifiesto });

        _releases.Add(new { tag_name = $"{id}-v{version}", draft = false, prerelease = false, assets });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
    {
        if (SinRed) throw new HttpRequestException("sin red");
        string url = pedido.RequestUri!.ToString();
        if (url == UrlReleases)
        {
            ConsultasDeReleases++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(_releases)) };
        }
        if (!_archivos.TryGetValue(url, out byte[]? datos)) return new HttpResponseMessage(HttpStatusCode.NotFound);
        if (Freno is not null && url.EndsWith(".zip", StringComparison.Ordinal)) await Freno.Task.WaitAsync(ct);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(datos) };
    }

    private static byte[] CrearZip(string id)
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            Escribir(zip, $"{id}.dll", "dll de prueba");
            Escribir(zip, $"{id}.addin",
                $"<?xml version=\"1.0\"?><RevitAddIns><AddIn Type=\"Application\"><Assembly>{id}.dll</Assembly></AddIn></RevitAddIns>");
        }
        return memoria.ToArray();
    }

    private static void Escribir(ZipArchive zip, string nombre, string contenido)
    {
        using Stream flujo = zip.CreateEntry(nombre).Open();
        flujo.Write(Encoding.UTF8.GetBytes(contenido));
    }
}
