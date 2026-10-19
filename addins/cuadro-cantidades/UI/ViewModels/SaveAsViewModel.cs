using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Interfaces;

namespace CQEING_CuadroCantidades.UI.ViewModels;

/// <summary>
/// Modelo de vista para el diálogo "Guardar como", que permite al usuario definir un nuevo nombre de versión o estructura
/// para el presupuesto actual y guardarlo por separado en la base de datos.
/// </summary>
public sealed partial class SaveAsViewModel : ObservableObject
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentProjectContext _projectContext;

    /// <summary>
    /// Obtiene o establece el nombre de la versión o estructura ingresado por el usuario.
    /// </summary>
    [ObservableProperty]
    private string _versionName = string.Empty;

    /// <summary>
    /// Evento que solicita el cierre de la vista vinculada.
    /// </summary>
    public event Action? RequestClose;

    /// <summary>
    /// Obtiene un valor que indica si la acción de guardar fue confirmada por el usuario.
    /// </summary>
    public bool IsConfirmed { get; private set; }

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="SaveAsViewModel"/>.
    /// </summary>
    /// <param name="unitOfWork">Unidad de trabajo para acceder a los repositorios de datos.</param>
    /// <param name="projectContext">Contexto del proyecto actual.</param>
    public SaveAsViewModel(IUnitOfWork unitOfWork, ICurrentProjectContext projectContext)
    {
        _unitOfWork = unitOfWork;
        _projectContext = projectContext;
    }

    /// <summary>
    /// Comando asíncrono que valida el nombre de la versión y confirma la acción de guardado.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmAsync()
    {
        // Validar que no esté vacío el nombre
        if (string.IsNullOrWhiteSpace(VersionName))
        {
            System.Windows.MessageBox.Show(
                "Por favor ingrese un nombre para la nueva estructura.",
                "Guardar Como",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning
            );
            return;
        }

        // Obtener el documento de Revit activo desde el contexto
        var projectPath = _projectContext.RevitDocumentPath;
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            // Buscar si existe el proyecto en la base de datos
            var projects = await _unitOfWork.Projects.FindAsync(p => p.RevitDocumentPath == projectPath);
            var project = projects.FirstOrDefault();
            if (project != null)
            {
                // Buscar si ya existe un presupuesto con el mismo nombre para este proyecto
                var existingBudgets = await _unitOfWork.Budgets.FindAsync(b => 
                    b.ProjectId == project.Id && b.VersionName.ToLower() == VersionName.Trim().ToLower());

                if (existingBudgets.Any())
                {
                    System.Windows.MessageBox.Show(
                        "Ya existe una estructura con ese nombre en este proyecto. Por favor elija un nombre diferente.",
                        "Nombre Duplicado",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning
                    );
                    return;
                }
            }
        }

        // Si es válido y no está duplicado, marcar como confirmado y solicitar cierre
        IsConfirmed = true;
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Comando que cancela la operación actual.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        IsConfirmed = false;
        RequestClose?.Invoke();
    }
}
