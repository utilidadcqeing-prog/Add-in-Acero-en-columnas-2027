using CQEING.Compartido;

namespace CQEING.Gestor.Core;

/// <param name="InstaladaPorAnio">Año de Revit → versión que el .addin de ese año tiene activa.</param>
/// <param name="EnDiscoPorAnio">Año de Revit → versiones ya descargadas en este equipo (a las que se puede volver sin red).</param>
public sealed record EstadoAddin(
    EntradaCatalogo Addin,
    IReadOnlyDictionary<string, string> InstaladaPorAnio,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? EnDiscoPorAnio = null)
{
    public string? Instalada(string anio) => InstaladaPorAnio.GetValueOrDefault(anio);

    /// <summary>La versión ya está descargada para ese año: volver a ella no necesita red.</summary>
    public bool EstaEnDisco(string anio, string version) =>
        EnDiscoPorAnio is not null && EnDiscoPorAnio.TryGetValue(anio, out IReadOnlyList<string>? versiones) && versiones.Contains(version);

    /// <summary>Hay una versión más nueva que la instalada en ese año y trae paquete para ese Revit.</summary>
    public bool HayActualizacion(string anio) =>
        VersionAddin.Interpretar(Instalada(anio)) is { } actual
        && Addin.Ultima.Version.CompareTo(actual) > 0
        && Addin.Ultima.Manifiesto.PaqueteParaRevit(anio) is not null;
}

/// <param name="AniosRevit">Años de Revit instalados en este equipo.</param>
/// <param name="Avisos">Textos para el usuario: lista guardada en uso, releases omitidas, etc.</param>
/// <param name="DesdeCache">La lista de GitHub viene de la caché (sin conexión o límite): solo se puede volver a versiones en disco.</param>
/// <param name="UltimoGestor">Última versión publicada del gestor, para su autoactualización.</param>
public sealed record ResumenGestor(
    IReadOnlyList<string> AniosRevit,
    IReadOnlyList<EstadoAddin> Addins,
    IReadOnlyList<string> Avisos,
    EtiquetaRelease? UltimoGestor,
    bool DesdeCache = false);

/// <summary>Punto de entrada de la ventana: qué hay publicado, qué hay instalado y cómo instalar.</summary>
public sealed class GestorAddins(FuenteReleases fuente, ConstructorCatalogo catalogo, Instalador instalador, RutasGestor rutas, string programFiles)
{
    public static GestorAddins Crear()
    {
        HttpClient http = ClienteHttp.Crear();
        RutasGestor rutas = RutasGestor.DelUsuario();
        return new GestorAddins(
            new FuenteReleases(http, rutas.CarpetaCache),
            new ConstructorCatalogo(http, rutas.CarpetaCache),
            new Instalador(http, rutas),
            rutas,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
    }

    public async Task<ResumenGestor> ConsultarAsync(CancellationToken ct = default)
    {
        ResultadoReleases releases = await fuente.ObtenerAsync(ct);
        ResultadoCatalogo resultado = await catalogo.ConstruirAsync(releases.Releases, ct);
        IReadOnlyList<string> anios = DetectorRevit.AniosInstalados(programFiles);

        foreach (string anio in anios) AnotarUltimas(resultado.Addins, anio);

        var avisos = new List<string>();
        if (releases.Aviso is not null) avisos.Add(releases.Aviso);
        avisos.AddRange(resultado.Avisos);

        return new ResumenGestor(anios, LeerEstados(resultado.Addins, anios), avisos, ConstructorCatalogo.UltimoGestor(releases.Releases), releases.DesdeCache);
    }

    /// <summary>
    /// Novedades para el aviso dentro de Revit, solo del año de ese Revit. Si la lista se consultó hace menos de
    /// <paramref name="vigencia"/> se usa la guardada: abrir Revit varias veces seguidas no gasta el cupo de GitHub.
    /// </summary>
    public async Task<IReadOnlyList<Novedad>> NovedadesAsync(string anio, TimeSpan vigencia, CancellationToken ct = default)
    {
        ResultadoReleases releases = await fuente.ObtenerRecienteAsync(vigencia, ct);
        ResultadoCatalogo resultado = await catalogo.ConstruirAsync(releases.Releases, ct);
        AnotarUltimas(resultado.Addins, anio);
        return NovedadesRevit.De(LeerEstados(resultado.Addins, [anio]), anio);
    }

    /// <summary>
    /// Anota la última versión de cada add-in con paquete para ese año: un add-in más viejo no se deja usar al abrir Revit
    /// (actualización obligatoria). También con la lista guardada: lo que ya se supo sigue valiendo sin red.
    /// </summary>
    private void AnotarUltimas(IReadOnlyList<EntradaCatalogo> catalogo, string anio) =>
        ActualizacionObligatoria.Escribir(rutas.CarpetaCqeing, anio, catalogo
            .Where(addin => addin.Ultima.Manifiesto.PaqueteParaRevit(anio) is not null)
            .ToDictionary(addin => addin.Id, addin => addin.Ultima.Version.ToString()));

    /// <summary>
    /// Vuelve a leer del disco qué está instalado y descargado, con el mismo catálogo y SIN consultar GitHub
    /// (sin token, cada consulta descuenta del cupo de 60 por hora). Es lo que usa la ventana después de instalar.
    /// </summary>
    public ResumenGestor ReleerEstado(ResumenGestor resumen) =>
        resumen with { Addins = LeerEstados(resumen.Addins.Select(a => a.Addin).ToList(), resumen.AniosRevit) };

    private List<EstadoAddin> LeerEstados(IReadOnlyList<EntradaCatalogo> catalogo, IReadOnlyList<string> anios)
    {
        IReadOnlyList<AddinInstalado> instalados = LectorEstado.Leer(rutas);
        return catalogo
            .Select(addin => new EstadoAddin(
                addin,
                instalados.Where(i => i.Id == addin.Id).ToDictionary(i => i.Anio, i => i.Version),
                anios.ToDictionary(anio => anio, anio => LectorEstado.VersionesEnDisco(rutas, addin.Id, anio))))
            .ToList();
    }

    /// <summary>Instala, actualiza o vuelve a <paramref name="version"/> en ese año de Revit.</summary>
    public Task InstalarAsync(VersionPublicada version, string anio, CancellationToken ct = default, IProgress<double>? progreso = null) =>
        instalador.InstalarAsync(version, anio, ct, progreso);

    /// <summary>Atajo de <see cref="EstadoAddin.HayActualizacion"/> para la ventana.</summary>
    public static bool HayActualizacion(EstadoAddin estado, string anio) => estado.HayActualizacion(anio);
}
