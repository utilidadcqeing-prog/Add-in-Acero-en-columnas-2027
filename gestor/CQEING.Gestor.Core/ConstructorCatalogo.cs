namespace CQEING.Gestor.Core;

public sealed record VersionPublicada(Manifiesto Manifiesto, VersionAddin Version, IReadOnlyDictionary<string, Uri> Archivos)
{
    public Uri UrlDe(PaqueteManifiesto paquete) =>
        Archivos.TryGetValue(paquete.Archivo, out Uri? url)
            ? url
            : throw new GestorException(TipoError.PaqueteInvalido, $"La versión {Version} de {Manifiesto.Nombre} no trae el archivo {paquete.Archivo}.");
}

/// <param name="Versiones">De la más nueva a la más vieja; nunca está vacía.</param>
public sealed record EntradaCatalogo(string Id, string Nombre, string Descripcion, IReadOnlyList<VersionPublicada> Versiones)
{
    public VersionPublicada Ultima => Versiones[0];
}

/// <param name="Avisos">Releases que no se pudieron usar, explicadas para el usuario.</param>
public sealed record ResultadoCatalogo(IReadOnlyList<EntradaCatalogo> Addins, IReadOnlyList<string> Avisos);

/// <summary>
/// Agrupa las releases estables por add-in y lee el manifest.json de las más nuevas. Una release publicada no
/// cambia, así que cada manifiesto se descarga una vez y queda en la caché. Solo se guarda lo que pasó la
/// validación; si un manifiesto guardado se daña, se descarta y se vuelve a pedir.
/// </summary>
public sealed class ConstructorCatalogo(HttpClient http, string carpetaCache)
{
    public const int VersionesPorAddin = 5;

    private string CarpetaManifiestos => Path.Combine(carpetaCache, "manifiestos");

    public async Task<ResultadoCatalogo> ConstruirAsync(IReadOnlyList<ReleaseGitHub> releases, CancellationToken ct = default)
    {
        var avisos = new List<string>();
        var addins = new List<EntradaCatalogo>();
        foreach (var grupo in Estables(releases).Where(x => x.Etiqueta.Id != EtiquetaRelease.IdGestor).GroupBy(x => x.Etiqueta.Id))
        {
            var versiones = new List<VersionPublicada>();
            foreach (var (release, etiqueta) in grupo.OrderByDescending(x => x.Etiqueta.Version).Take(VersionesPorAddin))
            {
                VersionPublicada? version = await LeerVersionAsync(release, etiqueta, avisos, ct);
                if (version is not null) versiones.Add(version);
            }
            if (versiones.Count > 0)
                addins.Add(new EntradaCatalogo(grupo.Key, versiones[0].Manifiesto.Nombre, versiones[0].Manifiesto.Descripcion, versiones));
        }
        return new ResultadoCatalogo(addins.OrderBy(a => a.Nombre, StringComparer.CurrentCulture).ToList(), avisos);
    }

    /// <summary>Última versión estable del gestor (etiquetas gestor-vX.Y.Z), para su autoactualización.</summary>
    public static EtiquetaRelease? UltimoGestor(IReadOnlyList<ReleaseGitHub> releases) =>
        Estables(releases).Where(x => x.Etiqueta.Id == EtiquetaRelease.IdGestor).Select(x => x.Etiqueta).MaxBy(e => e.Version);

    private static IEnumerable<(ReleaseGitHub Release, EtiquetaRelease Etiqueta)> Estables(IReadOnlyList<ReleaseGitHub> releases)
    {
        foreach (ReleaseGitHub release in releases)
        {
            if (release.Borrador || release.Prerelease) continue;
            if (EtiquetaRelease.Interpretar(release.Etiqueta) is { Version.EsEstable: true } etiqueta)
                yield return (release, etiqueta);
        }
    }

    private async Task<VersionPublicada?> LeerVersionAsync(ReleaseGitHub release, EtiquetaRelease etiqueta, List<string> avisos, CancellationToken ct)
    {
        // Si hay nombres repetidos gana el primero; una release mal armada no debe romper el catálogo entero.
        var archivos = new Dictionary<string, Uri>(StringComparer.Ordinal);
        foreach (ArchivoRelease archivo in release.Archivos ?? [])
            archivos.TryAdd(archivo.Nombre, archivo.Url);

        // La etiqueta ya pasó por EtiquetaRelease.Interpretar: solo trae minúsculas, dígitos, guiones y puntos.
        string guardado = Path.Combine(CarpetaManifiestos, $"{release.Etiqueta}.json");

        string? json = LeerGuardado(guardado);
        bool vieneDeCache = json is not null;
        json ??= await DescargarManifiestoAsync(release.Etiqueta, archivos, avisos, ct);
        if (json is null) return null;

        Manifiesto manifiesto;
        try
        {
            manifiesto = SerializadorManifiesto.Leer(json);
        }
        catch (GestorException e)
        {
            if (vieneDeCache) BorrarSinFallar(guardado); // quedó dañado en el disco: la próxima consulta lo pide de nuevo
            avisos.Add($"{release.Etiqueta}: {e.Message}");
            return null;
        }
        if (manifiesto.Id != etiqueta.Id || manifiesto.Version != etiqueta.Version.ToString())
        {
            if (vieneDeCache) BorrarSinFallar(guardado);
            avisos.Add($"{release.Etiqueta}: su manifest.json dice {manifiesto.Id} {manifiesto.Version}; se omite.");
            return null;
        }

        if (!vieneDeCache) Guardar(guardado, json); // solo se guarda lo que pasó la validación
        return new VersionPublicada(manifiesto, etiqueta.Version, archivos);
    }

    private static string? LeerGuardado(string ruta)
    {
        try
        {
            return File.Exists(ruta) ? File.ReadAllText(ruta) : null;
        }
        catch (IOException)
        {
            return null; // ilegible: se vuelve a descargar
        }
    }

    private void Guardar(string ruta, string json)
    {
        try
        {
            Directory.CreateDirectory(CarpetaManifiestos);
            string temporal = ruta + ".tmp";
            File.WriteAllText(temporal, json);
            File.Move(temporal, ruta, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // La caché es una ayuda: si no se puede escribir, el catálogo igual funciona.
        }
    }

    private static void BorrarSinFallar(string ruta)
    {
        try
        {
            File.Delete(ruta);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task<string?> DescargarManifiestoAsync(string etiqueta, Dictionary<string, Uri> archivos, List<string> avisos, CancellationToken ct)
    {
        if (!archivos.TryGetValue(Repositorio.ArchivoManifiesto, out Uri? url))
        {
            avisos.Add($"{etiqueta}: la release no trae {Repositorio.ArchivoManifiesto}; se omite.");
            return null;
        }
        try
        {
            return await http.GetStringAsync(url, ct);
        }
        catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            avisos.Add($"{etiqueta}: no se pudo descargar su manifiesto; se omite por ahora.");
            return null;
        }
    }
}
