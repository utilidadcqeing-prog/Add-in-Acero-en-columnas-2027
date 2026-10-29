using System.Windows;
using System.Windows.Threading;
using CQEING.Gestor.Core;
using Velopack;

namespace CQEING.Gestor;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        RutasGestor rutas = RutasGestor.DelUsuario();

        // Debe ir primero: atiende los pasos de instalación y actualización que lanza Velopack (y puede salir aquí mismo).
        // Al desinstalar, el botón del gestor se va de Revit con él.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => new RegistroComplementoRevit(rutas).Quitar())
            .Run();

        // Con el botón dentro de Revit es fácil abrirlo dos veces: una sola ventana, la segunda trae al frente la abierta.
        using var unaSolaVentana = new UnaSolaVentana();
        if (!unaSolaVentana.EsLaPrimera)
        {
            UnaSolaVentana.TraerAlFrenteLaAbierta();
            return;
        }

        // En segundo plano y sin esperar: si falla no afecta al gestor (queda anotado y se reintenta al abrirlo otra vez).
        // Borra las versiones viejas (sin licencia o inactivas) y, después de autoactualizarse, reabre los Revit que cerró
        // para eso, ya con el componente nuevo registrado.
        ReinicioRevit reinicio = ReinicioRevit.Crear();
        _ = Task.Run(() =>
        {
            try
            {
                LimpiezaVersionesViejas.Ejecutar(rutas);
                ComplementoEnRevit.Registrar(rutas);
            }
            finally
            {
                reinicio.Reabrir(reinicio.TomarPendientes());
            }
        });

        Application aplicacion = CrearAplicacion();
        aplicacion.Run(new VentanaPrincipal(new VistaGestor(
            GestorAddins.Crear(), new ActualizadorVelopack(), reinicio, RenovadorLicencia.Crear(ClienteHttp.Crear()))));
    }

    /// <summary>La aplicación WPF con el tema del gestor. Un error no previsto se muestra en vez de cerrar la ventana en silencio.</summary>
    public static Application CrearAplicacion()
    {
        var aplicacion = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        aplicacion.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/CQEING.Gestor;component/Tema.xaml", UriKind.Relative),
        });
        aplicacion.DispatcherUnhandledException += MostrarErrorNoPrevisto;
        return aplicacion;
    }

    private static void MostrarErrorNoPrevisto(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Ocurrió un error no previsto:\n\n{e.Exception.Message}\n\nPuede seguir usando el gestor; si se repite, ciérrelo y vuelva a abrirlo.",
            "Gestor de add-ins CQEING",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
