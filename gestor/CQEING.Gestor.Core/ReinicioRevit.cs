using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CQEING.Gestor.Core;

/// <summary>Un Revit abierto en este equipo.</summary>
public sealed record RevitAbierto(int IdProceso, string Anio, string Ejecutable);

/// <summary>Un Revit que se cerró para cargar una versión nueva y hay que volver a abrir, con su modelo si había uno.</summary>
public sealed record RevitParaReabrir(string Ejecutable, string? Modelo);

/// <param name="Cerrados">Los que se cerraron.</param>
/// <param name="SiguenAbiertos">Años cuyo Revit no se cerró: el usuario canceló al preguntar si guardaba, o Revit estaba ocupado.</param>
public sealed record ResultadoCierre(IReadOnlyList<RevitParaReabrir> Cerrados, IReadOnlyList<string> SiguenAbiertos);

/// <summary>Los procesos de Revit del equipo. Es una interfaz para poder probar <see cref="ReinicioRevit"/> sin Revit.</summary>
public interface IProcesosRevit
{
    IReadOnlyList<RevitAbierto> Abiertos();

    /// <summary>Pide el cierre como si el usuario hiciera clic en la X: Revit pregunta si guardar los cambios.</summary>
    void PedirCierre(int idProceso);

    /// <summary>true si el proceso terminó dentro de <paramref name="espera"/>.</summary>
    Task<bool> EsperarCierreAsync(int idProceso, TimeSpan espera, CancellationToken ct);

    /// <summary>Abre Revit, con el modelo si se da. false si no se pudo.</summary>
    bool Abrir(string ejecutable, string? modelo);
}

/// <summary>
/// Reinicia Revit para que cargue la versión nueva de un add-in o del componente del gestor: Revit solo carga los
/// add-ins al abrirse. Nunca lo cierra a la fuerza: pide el cierre normal y, si el usuario cancela, Revit sigue abierto.
/// Plan: docs/planes/2026-10-25-gestor-reinicia-revit.md.
/// </summary>
public sealed class ReinicioRevit(IProcesosRevit procesos, RutasGestor rutas, TimeSpan? espera = null, Func<DateTime>? ahora = null)
{
    /// <summary>Tiempo para que el usuario decida si guarda y Revit termine de cerrarse.</summary>
    public static readonly TimeSpan EsperaPorDefecto = TimeSpan.FromMinutes(3);

    /// <summary>Una lista de Revit por reabrir más vieja que esto no es de la actualización que acaba de pasar: se ignora.</summary>
    public static readonly TimeSpan VigenciaPendientes = TimeSpan.FromMinutes(15);

    private static readonly Regex CarpetaRevit = new(@"^Revit ([0-9]{4})$", RegexOptions.CultureInvariant);

    private readonly TimeSpan _espera = espera ?? EsperaPorDefecto;
    private readonly Func<DateTime> _ahora = ahora ?? (() => DateTime.UtcNow);

    public static ReinicioRevit Crear() => new(new ProcesosRevit(), RutasGestor.DelUsuario());

    /// <summary>«...\Autodesk\Revit 2027\Revit.exe» → «2027»; null si no es la carpeta de un Revit.</summary>
    public static string? AnioDe(string ejecutable)
    {
        Match m = CarpetaRevit.Match(Path.GetFileName(Path.GetDirectoryName(ejecutable)) ?? string.Empty);
        return m.Success ? m.Groups[1].Value : null;
    }

    public bool HayAbierto(string anio) => procesos.Abiertos().Any(r => r.Anio == anio);

    /// <summary>Cierra los Revit abiertos de esos años (todos si <paramref name="anios"/> es null), uno por uno.</summary>
    public async Task<ResultadoCierre> CerrarAsync(IReadOnlyCollection<string>? anios, CancellationToken ct = default)
    {
        var cerrados = new List<RevitParaReabrir>();
        var siguenAbiertos = new List<string>();
        foreach (RevitAbierto revit in procesos.Abiertos().Where(r => anios is null || anios.Contains(r.Anio)))
        {
            // Antes de pedir el cierre: al cerrar el modelo, el componente borra su anotación.
            string? modelo = ModeloActivo(revit.IdProceso);
            procesos.PedirCierre(revit.IdProceso);
            if (await procesos.EsperarCierreAsync(revit.IdProceso, _espera, ct))
                cerrados.Add(new RevitParaReabrir(revit.Ejecutable, modelo));
            else if (!siguenAbiertos.Contains(revit.Anio))
                siguenAbiertos.Add(revit.Anio);
        }
        return new ResultadoCierre(cerrados, siguenAbiertos);
    }

    /// <summary>Vuelve a abrir esos Revit. Un modelo que ya no existe no se pasa: se abre solo Revit. false si alguno no abrió.</summary>
    public bool Reabrir(IEnumerable<RevitParaReabrir> revits)
    {
        bool todos = true;
        foreach (RevitParaReabrir revit in revits)
            todos &= procesos.Abrir(revit.Ejecutable, revit.Modelo is not null && File.Exists(revit.Modelo) ? revit.Modelo : null);
        return todos;
    }

    /// <summary>Antes de reiniciar el gestor para actualizarlo: el gestor nuevo los reabre con <see cref="TomarPendientes"/>.</summary>
    public void GuardarPendientes(IReadOnlyList<RevitParaReabrir> revits)
    {
        if (revits.Count == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(rutas.ArchivoReabrirRevit)!);
        File.WriteAllText(rutas.ArchivoReabrirRevit, JsonSerializer.Serialize(new Pendientes(_ahora(), revits)));
    }

    /// <summary>Los Revit que dejó anotados el gestor anterior, y borra la anotación. Vacío si no hay, está dañada o es vieja.</summary>
    public IReadOnlyList<RevitParaReabrir> TomarPendientes()
    {
        string archivo = rutas.ArchivoReabrirRevit;
        if (!File.Exists(archivo)) return [];
        try
        {
            Pendientes? pendientes = JsonSerializer.Deserialize<Pendientes>(File.ReadAllText(archivo));
            return pendientes is not null && _ahora() - pendientes.Fecha < VigenciaPendientes ? pendientes.Revits : [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
        finally
        {
            BorrarSinFallar(archivo);
        }
    }

    private string? ModeloActivo(int idProceso)
    {
        try
        {
            string archivo = rutas.ArchivoSesionRevit(idProceso);
            string? modelo = File.Exists(archivo) ? File.ReadAllText(archivo).Trim() : null;
            return string.IsNullOrEmpty(modelo) ? null : modelo;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null; // se reabre solo Revit
        }
    }

    private static void BorrarSinFallar(string archivo)
    {
        try
        {
            File.Delete(archivo);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record Pendientes(DateTime Fecha, IReadOnlyList<RevitParaReabrir> Revits);
}

/// <summary>
/// Lo que escribe el componente del gestor dentro de Revit: la ruta del modelo activo de ese proceso, o nada. Solo un
/// archivo normal en disco y sin trabajo compartido (abrir un modelo central por su ruta puede ser un riesgo).
/// </summary>
public static class SesionRevit
{
    public static void Anotar(RutasGestor rutas, int idProceso, string? modelo)
    {
        string archivo = rutas.ArchivoSesionRevit(idProceso);
        try
        {
            if (modelo is null)
            {
                File.Delete(archivo);
                return;
            }
            Directory.CreateDirectory(rutas.CarpetaSesionesRevit);
            File.WriteAllText(archivo, modelo);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Sin anotación, el gestor reabre solo Revit.
        }
    }
}

/// <summary>Los procesos reales de Revit (Revit.exe dentro de «...\Autodesk\Revit AAAA\»).</summary>
public sealed class ProcesosRevit : IProcesosRevit
{
    public IReadOnlyList<RevitAbierto> Abiertos()
    {
        var abiertos = new List<RevitAbierto>();
        foreach (Process proceso in Process.GetProcessesByName("Revit"))
        {
            using (proceso)
            {
                if (Ruta(proceso) is { } ruta && ReinicioRevit.AnioDe(ruta) is { } anio)
                    abiertos.Add(new RevitAbierto(proceso.Id, anio, ruta));
            }
        }
        return abiertos;
    }

    public void PedirCierre(int idProceso)
    {
        try
        {
            using Process proceso = Process.GetProcessById(idProceso);
            proceso.CloseMainWindow();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            // Ya se cerró.
        }
    }

    public async Task<bool> EsperarCierreAsync(int idProceso, TimeSpan espera, CancellationToken ct)
    {
        Process proceso;
        try
        {
            proceso = Process.GetProcessById(idProceso);
        }
        catch (ArgumentException)
        {
            return true; // ya se cerró
        }

        using (proceso)
        using (var limite = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            limite.CancelAfter(espera);
            try
            {
                await proceso.WaitForExitAsync(limite.Token);
                return true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return false;
            }
        }
    }

    public bool Abrir(string ejecutable, string? modelo)
    {
        try
        {
            var inicio = new ProcessStartInfo(ejecutable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(ejecutable)! };
            if (modelo is not null) inicio.Arguments = $"\"{modelo}\"";
            using Process? proceso = Process.Start(inicio);
            return true;
        }
        catch (Exception e) when (e is Win32Exception or IOException)
        {
            return false;
        }
    }

    private static string? Ruta(Process proceso)
    {
        try
        {
            return proceso.MainModule?.FileName;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return null; // sin permiso o ya se cerró
        }
    }
}
