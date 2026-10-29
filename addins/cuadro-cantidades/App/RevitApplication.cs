using System;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using CQEING.Compartido;
using CQEING_CuadroCantidades.Commands;
using CQEING_CuadroCantidades.Core;
using CQEING_CuadroCantidades.Services;

namespace CQEING_CuadroCantidades.App;

/// <summary>
/// Clase de entrada de la aplicación externa para Revit.
/// Configura la cinta de opciones (Ribbon) y gestiona los eventos de inicio y apagado del complemento.
/// </summary>
public sealed class RevitApplication : IExternalApplication
{
    /// <summary>
    /// Se ejecuta cuando Revit inicia la aplicación externa.
    /// Inicializa los servicios del ciclo de vida del documento, el ejecutor de eventos y crea la interfaz Ribbon.
    /// </summary>
    /// <param name="application">Instancia de la aplicación controlada de Revit.</param>
    /// <returns>Resultado de la ejecución del inicio de la aplicación.</returns>
    public Result OnStartup(UIControlledApplication application)
    {
        // Sin licencia vigente no se inicia nada ni se crea el botón; el aviso lo da el componente del gestor (issue #189).
        // OnShutdown no se ve afectado: cada servicio comprueba si se inició antes de apagarse.
        if (!Licencia.Vigente(out _)) return Result.Succeeded;
        // Con una versión nueva publicada, tampoco: hay que actualizar desde el gestor (actualización obligatoria).
        if (!BloqueoPorVersion.AlDia(application, AppConstants.RibbonTabName)) return Result.Succeeded;

        // Inicializa el gestor de ejecución de eventos asíncronos en el hilo de Revit
        RevitEventExecutor.Initialize();

        // Evita que un error en las ventanas del add-in cierre Revit
        UiExceptionGuard.Initialize();

        // Inicializa el servicio que rastrea la apertura, cierre y cambio de documentos
        DocumentLifecycleService.Initialize(application);

        // Crea los elementos de la interfaz de usuario en la cinta de opciones de Revit
        CreateRibbon(application);

        return Result.Succeeded;
    }

    /// <summary>
    /// Se ejecuta al cerrar Revit o descargar el complemento.
    /// Libera recursos, detiene el ciclo de vida de los documentos y cierra las ventanas abiertas.
    /// </summary>
    /// <param name="application">Instancia de la aplicación controlada de Revit.</param>
    /// <returns>Resultado de la ejecución de apagado de la aplicación.</returns>
    public Result OnShutdown(UIControlledApplication application)
    {
        // Detiene el servicio de ciclo de vida del documento
        DocumentLifecycleService.Shutdown(application);

        // Cierra cualquier ventana que se haya quedado abierta de forma no modal
        ModelessWindowService.Close();

        UiExceptionGuard.Shutdown();

        return Result.Succeeded;
    }

    /// <summary>
    /// Crea la pestaña, el panel y el botón de la aplicación en la cinta de opciones (Ribbon) de Revit.
    /// </summary>
    /// <param name="application">Instancia de la aplicación controlada de Revit.</param>
    private static void CreateRibbon(UIControlledApplication application)
    {
        // Intenta crear la pestaña personalizada en la cinta de opciones de Revit
        TryCreateRibbonTab(application, AppConstants.RibbonTabName);

        // Obtiene o crea el panel dentro de la pestaña personalizada
        RibbonPanel panel = GetOrCreateRibbonPanel(application);

        // Verifica si el botón del comando ya fue creado para evitar duplicados
        if (RibbonButtonExists(panel, AppConstants.CommandName))
        {
            return;
        }

        // Obtiene la ruta del ensamblado actual y el nombre completo de la clase del comando a ejecutar
        string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
        string commandClassName = typeof(OpenCuadroCantidadesCommand).FullName!;

        // Define la estructura y metadatos del botón
        PushButtonData buttonData = new(
            AppConstants.CommandName,
            AppConstants.RibbonButtonText,
            thisAssemblyPath,
            commandClassName
        )
        {
            ToolTip = "Abre el cuadro de cantidades del documento activo.",
            LongDescription =
                "Abre una ventana flotante para construir y revisar el presupuesto del documento activo.",
        };

        // Configura los íconos del botón buscando en la carpeta Resources
        string assemblyDir =
            System.IO.Path.GetDirectoryName(thisAssemblyPath) ?? AppContext.BaseDirectory;
        string imagePath = System.IO.Path.Combine(assemblyDir, "Resources", "rubik.png");

        if (System.IO.File.Exists(imagePath))
        {
            BitmapImage image = new BitmapImage(new Uri(imagePath));
            image.Freeze(); // Congela la imagen para mejorar el rendimiento en WPF/Revit
            buttonData.LargeImage = image;
            buttonData.Image = image;
        }

        // Agrega el botón al panel
        panel.AddItem(buttonData);
    }

    /// <summary>
    /// Busca y obtiene un panel existente en una pestaña específica, o crea uno nuevo en caso de no existir.
    /// </summary>
    /// <param name="application">Instancia de la aplicación controlada de Revit.</param>
    /// <returns>Instancia de RibbonPanel donde se añadirán los botones.</returns>
    private static RibbonPanel GetOrCreateRibbonPanel(UIControlledApplication application)
    {
        // Busca en los paneles existentes dentro de la pestaña personalizada
        foreach (RibbonPanel panel in application.GetRibbonPanels(AppConstants.RibbonTabName))
        {
            if (panel.Name == AppConstants.RibbonPanelName)
            {
                return panel;
            }
        }

        // Si no existe, crea un nuevo panel
        return application.CreateRibbonPanel(
            AppConstants.RibbonTabName,
            AppConstants.RibbonPanelName
        );
    }

    /// <summary>
    /// Comprueba si ya existe un botón con el nombre indicado dentro de un panel del Ribbon.
    /// </summary>
    /// <param name="panel">El panel del Ribbon donde buscar.</param>
    /// <param name="buttonName">El nombre del botón a buscar.</param>
    /// <returns>True si el botón existe, False de lo contrario.</returns>
    private static bool RibbonButtonExists(RibbonPanel panel, string buttonName)
    {
        foreach (RibbonItem item in panel.GetItems())
        {
            if (item.Name == buttonName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Intenta crear una pestaña personalizada en la cinta de opciones de Revit.
    /// Maneja la excepción si la pestaña ya ha sido creada por otro complemento de CQEING.
    /// </summary>
    /// <param name="application">Instancia de la aplicación controlada de Revit.</param>
    /// <param name="tabName">Nombre de la pestaña a crear.</param>
    private static void TryCreateRibbonTab(UIControlledApplication application, string tabName)
    {
        try
        {
            application.CreateRibbonTab(tabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // La pestaña ya puede existir si fue creada por otro complemento de CQEING
        }
    }
}
