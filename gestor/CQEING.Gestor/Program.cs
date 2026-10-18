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
        // Debe ir primero: atiende los pasos de instalación y actualización que lanza Velopack (y puede salir aquí mismo).
        VelopackApp.Build().Run();

        Application aplicacion = CrearAplicacion();
        aplicacion.Run(new VentanaPrincipal(new VistaGestor(GestorAddins.Crear(), new ActualizadorVelopack())));
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
