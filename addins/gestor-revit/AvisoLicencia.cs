using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows.Threading;
using CQEING.Compartido;
using CQEING.Gestor.Core;

namespace CQEING.GestorRevit;

/// <summary>
/// Al abrir Revit: si este equipo no tiene licencia vigente, avisa (<see cref="VentanaLicencia"/>) cuando Revit queda
/// libre, porque los add-ins no crean sus botones y no avisan (issue #189, Decisión 6). Además renueva la licencia en
/// segundo plano: lo que llegue sirve desde la próxima apertura de Revit, porque los add-ins ya se cargaron (Hallazgo 4).
/// </summary>
internal static class AvisoLicencia
{
    private const string ArchivoRegistro = "licencia-revit.log";

    public static void Empezar()
    {
        Dispatcher hiloRevit = Dispatcher.CurrentDispatcher;
        if (!Licencia.Vigente(out string motivo))
            _ = hiloRevit.InvokeAsync(() => Mostrar(motivo), DispatcherPriority.ApplicationIdle);

        string version = VersionDelComponente();
        Task.Run(async () =>
        {
            try
            {
                using HttpClient http = ClienteHttp.Crear();
                InfoLicencia info = await RenovadorLicencia.Crear(http).RenovarAsync(null, forzar: false, version);
                RegistroSoporte.Anotar(ArchivoRegistro, info.UltimaRespuesta == EstadoSolicitud.SinConexion
                    ? "No se pudo consultar la licencia (sin conexión con el servicio)."
                    : null);
            }
            catch (Exception e)
            {
                // Renovar es una ayuda: nunca debe molestar ni tumbar Revit.
                RegistroSoporte.Anotar(ArchivoRegistro, $"No se pudo renovar la licencia: {e.Message}");
            }
        });
    }

    private static void Mostrar(string motivo)
    {
        // Corre dentro del bucle de mensajes de Revit: una excepción aquí cerraría Revit.
        try
        {
            new VentanaLicencia(motivo).Mostrar(Process.GetCurrentProcess().MainWindowHandle);
        }
        catch (Exception e)
        {
            RegistroSoporte.Anotar(ArchivoRegistro, $"No se pudo mostrar el aviso de licencia: {e.Message}");
        }
    }

    /// <summary>La versión X.Y.Z con que el gestor compiló este componente; queda en la hoja («Versión gestor»).</summary>
    internal static string VersionDelComponente()
    {
        string? version = typeof(AvisoLicencia).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return (version ?? "0.0.0").Split('+')[0];
    }
}
