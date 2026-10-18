using CQEING.Gestor.Core;

namespace CQEING.Gestor.Tests.Apoyo;

/// <summary>Un «equipo» temporal con los años de Revit dados, el GitHub simulado y la ventana (sin abrirla).</summary>
internal sealed class Entorno : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "cqeing-gestor-app-" + Guid.NewGuid().ToString("N"));

    public Entorno(params string[] aniosRevit)
    {
        Rutas = new RutasGestor(Path.Combine(_raiz, "local"), Path.Combine(_raiz, "roaming"));
        string programFiles = Path.Combine(_raiz, "pf");
        foreach (string anio in aniosRevit)
        {
            Directory.CreateDirectory(Path.Combine(programFiles, "Autodesk", $"Revit {anio}"));
            File.WriteAllText(Path.Combine(programFiles, "Autodesk", $"Revit {anio}", "RevitAPI.dll"), "");
        }

        HttpClient http = ClienteHttp.Crear(GitHub);
        Gestor = new GestorAddins(
            new FuenteReleases(http, Rutas.CarpetaCache, GitHubFalso.Repo),
            new ConstructorCatalogo(http, Rutas.CarpetaCache),
            new Instalador(http, Rutas),
            Rutas,
            programFiles);
        Vista = new VistaGestor(Gestor, Actualizador, () => new DateTime(2026, 10, 17, 9, 5, 0));
    }

    public GitHubFalso GitHub { get; } = new();
    public ActualizadorFalso Actualizador { get; } = new();
    public RutasGestor Rutas { get; }
    public GestorAddins Gestor { get; }
    public VistaGestor Vista { get; }

    public TarjetaAddin Tarjeta(string id) => Vista.Tarjetas.Single(t => t.Id == id);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_raiz, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class ActualizadorFalso : IActualizadorGestor
{
    public string? Nueva { get; set; }
    public EtiquetaRelease? Recibido { get; private set; }
    public bool Aplicado { get; private set; }

    public Task<string?> BuscarAsync(EtiquetaRelease? ultimoPublicado)
    {
        Recibido = ultimoPublicado;
        return Task.FromResult(Nueva);
    }

    public Task AplicarAsync(IProgress<int> progreso)
    {
        Aplicado = true;
        return Task.CompletedTask;
    }
}
