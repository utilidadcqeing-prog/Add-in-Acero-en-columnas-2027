using System;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CQEING_CuadroCantidades.UI;
using Microsoft.Extensions.DependencyInjection;

namespace CQEING_CuadroCantidades.Services;

internal static class ModelessWindowService
{
    private static MainWindow? _window;
    private static Document? _sourceDocument;
    private static IServiceScope? _serviceScope;

    public static bool IsOpen => _window is not null;

    public static void Show(UIApplication uiApplication)
    {
        if (_window is not null)
        {
            if (_window.WindowState == System.Windows.WindowState.Minimized)
            {
                _window.WindowState = System.Windows.WindowState.Normal;
            }

            _window.Activate();
            return;
        }

        _sourceDocument = uiApplication.ActiveUIDocument?.Document;

        _serviceScope =
            CQEING_CuadroCantidades.App.AppServiceProvider.ServiceProvider?.CreateScope();
        var viewModel =
            _serviceScope?.ServiceProvider.GetService(
                typeof(CQEING_CuadroCantidades.UI.ViewModels.MainWindowViewModel)
            ) as CQEING_CuadroCantidades.UI.ViewModels.MainWindowViewModel;

        if (viewModel is null)
        {
            throw new InvalidOperationException(
                "No se pudo resolver MainWindowViewModel del contenedor de DI."
            );
        }

        _window = new MainWindow(viewModel);
        _window.Closed += (_, _) =>
        {
            _window = null;
            _sourceDocument = null;
            _serviceScope?.Dispose();
            _serviceScope = null;
        };

        WindowInteropHelper helper = new(_window) { Owner = uiApplication.MainWindowHandle };

        _window.Show();
    }

    public static void CloseIfDocument(Document document)
    {
        if (_sourceDocument is null || ReferenceEquals(_sourceDocument, document))
        {
            Close();
        }
    }

    public static void Close()
    {
        if (_window is null)
        {
            return;
        }

        MainWindow window = _window;
        _window = null;
        _sourceDocument = null;

        try
        {
            window.Close();
        }
        catch (InvalidOperationException)
        {
            // Revit may already be tearing down the WPF dispatcher.
        }
    }
}
