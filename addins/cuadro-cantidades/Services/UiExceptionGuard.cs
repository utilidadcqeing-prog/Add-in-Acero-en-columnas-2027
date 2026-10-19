using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Evita que un error de las ventanas del add-in cierre Revit.
/// Las ventanas del add-in corren en el hilo principal de Revit, así que una excepción sin atrapar
/// en WPF (por ejemplo dentro de una DataGrid) tumba todo el proceso. Este guardián la registra en
/// el log, la muestra en un mensaje y la marca como atendida.
/// Solo atiende errores del add-in: los de Revit u otros complementos siguen su curso normal.
/// </summary>
public static class UiExceptionGuard
{
    private static readonly Assembly AddinAssembly = typeof(UiExceptionGuard).Assembly;
    private static Dispatcher? _dispatcher;
    private static bool _showingMessage;

    /// <summary>
    /// Se suscribe al despachador del hilo actual. Debe llamarse en el hilo principal de Revit.
    /// </summary>
    public static void Initialize()
    {
        if (_dispatcher != null)
            return;

        _dispatcher = Dispatcher.CurrentDispatcher;
        _dispatcher.UnhandledException += OnUnhandledException;
    }

    public static void Shutdown()
    {
        if (_dispatcher == null)
            return;

        _dispatcher.UnhandledException -= OnUnhandledException;
        _dispatcher = null;
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (!IsFromAddin(e.Exception))
            return;

        e.Handled = true;
        ErrorLog.Write(nameof(UiExceptionGuard), "Error no controlado en una ventana del add-in", e.Exception);

        // Un error que se repite (p. ej. en cada redibujo) mostraría un mensaje tras otro
        if (_showingMessage)
            return;

        _showingMessage = true;
        try
        {
            MessageBox.Show(
                $"Ocurrió un error en el Cuadro de Cantidades y la acción no se completó:\n\n{e.Exception.Message}\n\n"
                    + "Revit sigue abierto. El detalle quedó en %AppData%\\CQEING_CuadroCantidades\\logs\\errors.log.",
                "Error en el Cuadro de Cantidades",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            _showingMessage = false;
        }
    }

    /// <summary>
    /// El error es del add-in si pasa por su código o si ocurrió con el foco en una de sus ventanas
    /// (los errores internos de WPF, como el de la DataGrid, no incluyen código del add-in en la pila).
    /// </summary>
    private static bool IsFromAddin(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current.StackTrace?.Contains(AddinAssembly.GetName().Name!, StringComparison.Ordinal) == true)
                return true;
        }

        var focusedWindow = Keyboard.FocusedElement is DependencyObject focused
            ? Window.GetWindow(focused)
            : null;
        return focusedWindow?.GetType().Assembly == AddinAssembly;
    }
}
