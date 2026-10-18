using CQEING.Gestor.Core;
using Velopack;
using Velopack.Sources;

namespace CQEING.Gestor;

/// <summary>
/// Velopack lee el releases.win.json de la última release gestor-vX.Y.Z. Se le da esa carpeta directamente
/// porque en el repo también hay releases de los add-ins, que no son del gestor.
/// </summary>
public sealed class ActualizadorVelopack : IActualizadorGestor
{
    private UpdateManager? _velopack;
    private UpdateInfo? _nueva;

    public async Task<string?> BuscarAsync(EtiquetaRelease? ultimoPublicado)
    {
        _nueva = null;
        if (ultimoPublicado is null) return null;

        var velopack = new UpdateManager(new SimpleWebSource($"https://github.com/{Repositorio.Nombre}/releases/download/{ultimoPublicado}/"));
        if (!velopack.IsInstalled) return null; // ejecutado con dotnet run, sin instalar con Setup.exe

        try
        {
            _nueva = await velopack.CheckForUpdatesAsync();
        }
        catch (Exception)
        {
            // Revisar el gestor nuevo es opcional: si falla (sin red, release a medio publicar), se revisa la próxima vez
            // y la ventana sigue funcionando con los add-ins.
            return null;
        }
        _velopack = velopack;
        return _nueva?.TargetFullRelease.Version.ToString();
    }

    public async Task AplicarAsync(IProgress<int> progreso)
    {
        if (_velopack is null || _nueva is null) return;
        await _velopack.DownloadUpdatesAsync(_nueva, progreso.Report);
        _velopack.ApplyUpdatesAndRestart(_nueva.TargetFullRelease);
    }
}
