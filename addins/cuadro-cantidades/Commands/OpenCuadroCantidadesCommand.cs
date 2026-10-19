using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CQEING_CuadroCantidades.App;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CQEING_CuadroCantidades.Commands;

/// <summary>
/// Comando externo que abre la interfaz gráfica de Cuadro de Cantidades.
/// Configurado con transacciones y regeneración manuales en Revit.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class OpenCuadroCantidadesCommand : IExternalCommand
{
    /// <summary>
    /// Ejecuta el comando externo dentro del contexto de Revit.
    /// Valida que haya un documento abierto, inicializa el contenedor IoC,
    /// asegura que la base de datos local en memoria esté inicializada,
    /// y finalmente abre la ventana principal de forma no modal.
    /// </summary>
    /// <param name="commandData">Datos del comando externo de Revit, que dan acceso a la aplicación y al documento activo.</param>
    /// <param name="message">Mensaje de error retornado a Revit si el comando falla.</param>
    /// <param name="elements">Conjunto de elementos seleccionados (en desuso pero requerido por la interfaz).</param>
    /// <returns>Resultado de la ejecución del comando (Succeeded o Failed).</returns>
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIApplication uiApplication = commandData.Application;

        // Verifica si hay algún documento de Revit activo
        if (uiApplication.ActiveUIDocument?.Document is null)
        {
            message = "Abre un documento de Revit antes de usar el cuadro de cantidades.";
            return Result.Failed;
        }

        string docPath = uiApplication.ActiveUIDocument.Document.PathName;
        
        // Inicializar el contenedor de Inyección de Dependencias
        AppServiceProvider.Initialize(docPath, uiApplication);

        // Inicializar la Base de Datos local en memoria de forma síncrona
        if (AppServiceProvider.ServiceProvider is not null)
        {
            using var scope = AppServiceProvider.ServiceProvider.CreateScope();
            var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
            dbService.InitializeDatabaseAsync().GetAwaiter().GetResult();
        }

        // Abre la ventana de forma no modal (modeless) pasando la instancia de la aplicación
        ModelessWindowService.Show(uiApplication);
        return Result.Succeeded;
    }
}
