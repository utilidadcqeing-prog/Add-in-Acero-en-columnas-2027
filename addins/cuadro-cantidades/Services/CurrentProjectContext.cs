using System;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Entities;
using CQEING_CuadroCantidades.UI.Models;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Implementación de <see cref="ICurrentProjectContext"/> que administra el estado y contexto actual del proyecto Revit activo,
/// incluyendo el presupuesto seleccionado, la fila de presupuesto activa y rutas de archivos asociadas.
/// </summary>
public sealed class CurrentProjectContext : ICurrentProjectContext
{
    private Project? _currentProject;
    private Budget? _currentBudget;
    private BudgetRow? _selectedBudgetRow;
    private string? _lastStructurePath;
    private string _loadedStructureName = "Sin estructura cargada";
    private string _newDescription = string.Empty;
    private string _newUnit = "M3";
    private string _revitDocumentPath = string.Empty;
    private bool _isDirty;

    /// <summary>
    /// Evento que se dispara cuando cambia cualquier propiedad del contexto del proyecto.
    /// </summary>
    public event EventHandler? ContextChanged;

    /// <summary>
    /// Obtiene o establece el proyecto actual.
    /// </summary>
    public Project? CurrentProject
    {
        get => _currentProject;
        set
        {
            if (_currentProject != value)
            {
                _currentProject = value;
                OnContextChanged();
            }
        }
    }

    /// <summary>
    /// Obtiene o establece el presupuesto actual asociado al contexto del proyecto.
    /// </summary>
    public Budget? CurrentBudget
    {
        get => _currentBudget;
        set
        {
            if (_currentBudget != value)
            {
                _currentBudget = value;
                OnContextChanged();
            }
        }
    }

    /// <summary>
    /// Obtiene o establece la fila de presupuesto seleccionada actualmente en la interfaz.
    /// </summary>
    public BudgetRow? SelectedBudgetRow
    {
        get => _selectedBudgetRow;
        set
        {
            if (_selectedBudgetRow != value)
            {
                _selectedBudgetRow = value;
                OnContextChanged();
            }
        }
    }

    /// <summary>
    /// Obtiene o establece la última ruta del archivo de estructura (Excel) que fue importada o exportada.
    /// </summary>
    public string? LastStructurePath
    {
        get => _lastStructurePath;
        set
        {
            if (_lastStructurePath != value)
            {
                _lastStructurePath = value;
                OnContextChanged();
            }
        }
    }

    /// <summary>
    /// Obtiene o establece el nombre descriptivo de la estructura cargada actualmente.
    /// </summary>
    public string LoadedStructureName
    {
        get => _loadedStructureName;
        set
        {
            if (_loadedStructureName != value)
            {
                _loadedStructureName = value;
                OnContextChanged();
            }
        }
    }

    /// <summary>
    /// Obtiene o establece el texto de descripción temporal para la creación de nuevos elementos de presupuesto.
    /// </summary>
    public string NewDescription
    {
        get => _newDescription;
        set
        {
            if (_newDescription != value)
            {
                _newDescription = value;
                OnContextChanged();
            }
        }
    }

    /// <summary>
    /// Obtiene o establece la unidad de medida por defecto para los nuevos elementos de presupuesto.
    /// </summary>
    public string NewUnit
    {
        get => _newUnit;
        set
        {
            if (_newUnit != value)
            {
                _newUnit = value;
                OnContextChanged();
            }
        }
    }

    /// <summary>
    /// Obtiene o establece la ruta del documento de Revit activo en el que se está trabajando.
    /// </summary>
    public string RevitDocumentPath
    {
        get => _revitDocumentPath;
        set
        {
            if (_revitDocumentPath != value)
            {
                _revitDocumentPath = value;
                OnContextChanged();
            }
        }
    }

    /// <summary>
    /// Obtiene o establece un indicador que señala si hay cambios pendientes por guardar en el contexto actual.
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
                // No disparamos ContextChanged para evitar bucles de autosave
            }
        }
    }

    /// <summary>
    /// Establece el contexto del proyecto y del presupuesto de forma simultánea, marcando el estado como limpio.
    /// </summary>
    /// <param name="project">El proyecto de Revit.</param>
    /// <param name="budget">El presupuesto asociado.</param>
    public void SetContext(Project project, Budget budget)
    {
        _currentProject = project;
        _currentBudget = budget;
        _isDirty = false;
        OnContextChanged();
    }

    /// <summary>
    /// Limpia por completo todas las propiedades del contexto, restableciéndolas a sus valores predeterminados.
    /// </summary>
    public void Clear()
    {
        _currentProject = null;
        _currentBudget = null;
        _selectedBudgetRow = null;
        _lastStructurePath = null;
        _loadedStructureName = "Sin estructura cargada";
        _newDescription = string.Empty;
        _newUnit = "M3";
        _revitDocumentPath = string.Empty;
        _isDirty = false;
        OnContextChanged();
    }

    /// <summary>
    /// Dispara el evento <see cref="ContextChanged"/> y establece el estado <see cref="IsDirty"/> en true.
    /// </summary>
    private void OnContextChanged()
    {
        _isDirty = true;
        ContextChanged?.Invoke(this, EventArgs.Empty);
    }
}
