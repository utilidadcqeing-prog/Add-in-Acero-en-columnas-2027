using System;
using System.IO;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Interfaces;
using CQEING_CuadroCantidades.Infrastructure.Data.Repositories;
using CQEING_CuadroCantidades.Infrastructure.Data.Context;
using CQEING_CuadroCantidades.Services;
using CQEING_CuadroCantidades.UI.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CQEING_CuadroCantidades.App;

/// <summary>
/// Proveedor de servicios de la aplicación (contenedor de Inyección de Dependencias).
/// Se encarga de inicializar y registrar todos los servicios, repositorios y ViewModels de la aplicación.
/// </summary>
public static class AppServiceProvider
{
    /// <summary>
    /// Instancia del proveedor de servicios configurado para la inyección de dependencias.
    /// </summary>
    public static IServiceProvider? ServiceProvider { get; private set; }

    /// <summary>
    /// Inicializa y configura el contenedor de inyección de dependencias (DI).
    /// Registra los contextos de base de datos, repositorios, servicios de aplicación y ViewModels.
    /// </summary>
    /// <param name="revitDocumentPath">Ruta del documento de Revit actualmente abierto.</param>
    /// <param name="uiApplication">Instancia de la aplicación de usuario de Revit (UIApplication).</param>
    public static void Initialize(
        string revitDocumentPath,
        Autodesk.Revit.UI.UIApplication uiApplication
    )
    {
        // Colección de servicios para registrar dependencias
        ServiceCollection services = new();

        // Configuración de la base de datos SQLite para persistencia local
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string baseDirectory = Path.Combine(appData, "CQEING_CuadroCantidades", "Databases");
        Directory.CreateDirectory(baseDirectory);

        string safeDocName = string.IsNullOrWhiteSpace(revitDocumentPath) 
            ? "Global" 
            : Path.GetFileNameWithoutExtension(revitDocumentPath);

        // Sanear el nombre para evitar caracteres inválidos en rutas
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            safeDocName = safeDocName.Replace(c, '_');
        }
            
        string dbPath = Path.Combine(baseDirectory, $"{safeDocName}.db");

        // Configuración del contexto de base de datos con SQLite
        services.AddDbContext<CuadroCantidadesDbContext>(options =>
        {
            options.UseSqlite($"Data Source={dbPath}");
        });

        // Registro de los patrones de persistencia (Repositorio y Unidad de Trabajo)
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        // Registro de servicios básicos de infraestructura
        services.AddScoped<IDatabaseService, DatabaseService>();
        services.AddScoped<IApiClient, ApiClient>();
        services.AddSingleton(uiApplication);

        // Registro de servicios de persistencia y gestión de estado del proyecto actual
        services.AddSingleton<ILocalStorageService, LocalStorageService>();
        services.AddSingleton<IPendingActivitySyncStore, PendingActivitySyncStore>();
        services.AddScoped<ICurrentProjectContext>(provider => new CurrentProjectContext
        {
            RevitDocumentPath = revitDocumentPath,
        });
        services.AddScoped<IProjectStateService, ProjectStateService>();
        services.AddScoped<IAutoSaveService, AutoSaveService>();

        // Registro de servicios para previsualización e imágenes captura de Revit
        services.AddScoped<IPreviewDebugImageStorageService, PreviewDebugImageStorageService>();
        services.AddScoped<IElementHighlightService, ElementHighlightService>();
        services.AddScoped<IRevitImageCaptureService, RevitImageCaptureService>();
        services.AddScoped<IActivityPreviewService, ActivityPreviewService>();
        services.AddScoped<IImageUploadService, CloudinaryUploadService>();

        // Registro de servicios reactivos y bus de eventos
        services.AddSingleton<IEventAggregator, EventAggregator>();
        services.AddSingleton<IAssignedElementsIndex, AssignedElementsIndex>();
        services.AddSingleton<IPreviewRegenerationQueue, PreviewRegenerationQueue>();
        services.AddScoped<IActivityElementRegistry, ActivityElementRegistry>();
        services.AddScoped<IElementAvailabilityService, ElementAvailabilityService>();
        services.AddScoped<ILivePreviewUpdater, LivePreviewUpdater>();

        // Registro de servicios de actualización y carga de previsualizaciones
        services.AddSingleton<IPreviewSessionContext, PreviewSessionContext>();
        services.AddScoped<IManualPreviewService, ManualPreviewService>();
        services.AddScoped<IActivityPreviewUpdater, ActivityPreviewUpdater>();
        services.AddScoped<IPreviewRefreshNotifier, PreviewRefreshNotifier>();
        services.AddSingleton<IUIThreadDispatcherService, UIThreadDispatcherService>();
        services.AddSingleton<IImageCacheBypassHelper, ImageCacheBypassHelper>();
        services.AddSingleton<IPreviewImageLoaderService, PreviewImageLoaderService>();
        services.AddScoped<IPreviewRefreshService, PreviewRefreshService>();

        // Registro de ViewModels para la interfaz gráfica
        services.AddTransient<MainWindowViewModel>();

        // Construcción del contenedor IoC
        ServiceProvider = services.BuildServiceProvider();
    }
}
