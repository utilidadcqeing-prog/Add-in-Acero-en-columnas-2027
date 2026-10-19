using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Entities;
using CQEING_CuadroCantidades.Core.Domain.Interfaces;

namespace CQEING_CuadroCantidades.UI.ViewModels;

/// <summary>
/// Modelo de vista para el diálogo de selección de estructura, que permite al usuario listar y elegir
/// una versión o estructura de presupuesto guardada previamente para el documento de Revit activo.
/// </summary>
public sealed partial class SelectStructureViewModel : ObservableObject
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentProjectContext _projectContext;
    private readonly IApiClient _apiClient;

    /// <summary>
    /// Obtiene la colección observable de presupuestos disponibles.
    /// </summary>
    public ObservableCollection<Budget> Budgets { get; } = new();

    /// <summary>
    /// Obtiene o establece el presupuesto seleccionado por el usuario.
    /// </summary>
    [ObservableProperty]
    private Budget? _selectedBudget;

    /// <summary>
    /// Obtiene o establece si la vista está en proceso de carga de presupuestos.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Número máximo de sugerencias que se muestran en el autocompletado.
    /// </summary>
    private const int MaxSuggestions = 6;

    /// <summary>
    /// Evita reabrir el autocompletado cuando el texto se asigna desde una sugerencia elegida.
    /// </summary>
    private bool _suppressSuggestions;

    /// <summary>
    /// Vista filtrada de <see cref="Budgets"/> según el texto de búsqueda.
    /// </summary>
    public ICollectionView BudgetsView { get; }

    /// <summary>
    /// Sugerencias del autocompletado para el texto de búsqueda actual.
    /// </summary>
    public ObservableCollection<BudgetSuggestion> Suggestions { get; } = new();

    /// <summary>
    /// Obtiene o establece el texto de búsqueda (código o nombre del presupuesto).
    /// </summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// Obtiene o establece si el desplegable de sugerencias está abierto.
    /// </summary>
    [ObservableProperty]
    private bool _isSuggestionsOpen;

    /// <summary>
    /// Obtiene si hay texto de búsqueda activo.
    /// </summary>
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>
    /// Obtiene si la búsqueda activa no arrojó resultados.
    /// </summary>
    public bool HasNoResults => !IsLoading && HasSearchText && VisibleCount == 0;

    /// <summary>
    /// Obtiene el resumen de resultados visibles frente al total.
    /// </summary>
    public string ResultSummary =>
        HasSearchText
            ? $"{VisibleCount} de {Budgets.Count} estructuras"
            : $"{Budgets.Count} estructuras";

    private int VisibleCount => BudgetsView.Cast<object>().Count();

    /// <summary>
    /// Evento que solicita el cierre de la vista vinculada.
    /// </summary>
    public event Action? RequestClose;

    /// <summary>
    /// Obtiene un valor que indica si la selección fue confirmada por el usuario.
    /// </summary>
    public bool IsConfirmed { get; private set; }

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="SelectStructureViewModel"/>.
    /// </summary>
    /// <param name="unitOfWork">Unidad de trabajo para acceder a los repositorios de datos.</param>
    /// <param name="projectContext">Contexto del proyecto actual.</param>
    /// <param name="apiClient">Cliente para consultar la API web externa.</param>
    public SelectStructureViewModel(
        IUnitOfWork unitOfWork,
        ICurrentProjectContext projectContext,
        IApiClient apiClient
    )
    {
        _unitOfWork = unitOfWork;
        _projectContext = projectContext;
        _apiClient = apiClient;

        BudgetsView = CollectionViewSource.GetDefaultView(Budgets);
        BudgetsView.Filter = item => item is Budget budget && Matches(budget, SearchText);
        Budgets.CollectionChanged += (_, _) => NotifyResultsChanged();

        // Disparar la carga de presupuestos de forma asíncrona en segundo plano
        _ = LoadBudgetsAsync();
    }

    /// <summary>
    /// Carga de manera asíncrona las estructuras de presupuesto guardadas que corresponden al proyecto activo de Revit.
    /// </summary>
    private async Task LoadBudgetsAsync()
    {
        IsLoading = true;
        try
        {
            var projectPath = _projectContext.RevitDocumentPath;
            if (string.IsNullOrWhiteSpace(projectPath))
                return;

            // Obtener presupuestos desde la API externa (Base de datos PostgreSQL en el backend)
            var apiBudgets = await _apiClient.GetBudgetsAsync(projectPath);
            if (apiBudgets != null)
            {
                var budgets = apiBudgets
                    .Select(ab => new Budget
                    {
                        Id = IdMapper.IntToGuid(ab.Id),
                        VersionName = ab.CodigoPresupuesto,
                        DisplayName = string.IsNullOrWhiteSpace(ab.Nombre)
                            ? ab.CodigoPresupuesto
                            : $"{ab.CodigoPresupuesto} - {ab.Nombre}",
                        CreatedAt = ab.FechaCreacion ?? DateTime.UtcNow,
                        IsActive = true,
                        RevitModelId = ab.ActualRevitModelId,
                    })
                    .ToList();

                // Actualizar la colección en el hilo principal de WPF
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Budgets.Clear();
                    // Ordenar por fecha de creación para mostrar el más reciente primero
                    foreach (var budget in budgets.OrderByDescending(b => b.CreatedAt))
                    {
                        Budgets.Add(budget);
                    }
                    SelectedBudget = Budgets.FirstOrDefault();
                });
            }
            else
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show(
                        "No se pudo conectar con el servidor para obtener los presupuestos.\n\nVerifique que el servicio esté encendido e intente de nuevo.",
                        "Servidor no disponible",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning
                    );
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[SelectStructureViewModel] Error al cargar presupuestos: {ex.Message}"
            );
        }
        finally
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                IsLoading = false;
            });
        }
    }

    /// <summary>
    /// Comando que valida la selección y confirma la acción de carga.
    /// </summary>
    [RelayCommand]
    private void Confirm()
    {
        if (SelectedBudget == null)
        {
            System.Windows.MessageBox.Show(
                "Selecciona una estructura para cargar.",
                "Seleccionar estructura",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning
            );
            return;
        }

        IsConfirmed = true;
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Comando que cancela la selección y cierra el diálogo.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        IsConfirmed = false;
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Aplica una sugerencia del autocompletado: fija el texto y selecciona el presupuesto.
    /// </summary>
    [RelayCommand]
    private void ApplySuggestion(BudgetSuggestion? suggestion)
    {
        if (suggestion == null)
            return;

        _suppressSuggestions = true;
        SearchText = suggestion.Code;
        _suppressSuggestions = false;

        IsSuggestionsOpen = false;
        SelectedBudget = suggestion.Budget;
    }

    /// <summary>
    /// Limpia el texto de búsqueda y muestra de nuevo todas las estructuras.
    /// </summary>
    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    partial void OnSearchTextChanged(string value)
    {
        BudgetsView.Refresh();
        NotifyResultsChanged();

        // Mantener una selección válida dentro de los resultados visibles
        if (SelectedBudget == null || !BudgetsView.Contains(SelectedBudget))
        {
            SelectedBudget = BudgetsView.Cast<Budget>().FirstOrDefault();
        }

        if (!_suppressSuggestions)
        {
            UpdateSuggestions(value);
        }
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(HasNoResults));

    private void UpdateSuggestions(string text)
    {
        Suggestions.Clear();

        if (!string.IsNullOrWhiteSpace(text))
        {
            string normalizedQuery = Normalize(text.Trim());
            var ranked = Budgets
                .Where(b => Matches(b, text))
                // Primero las coincidencias al inicio del código, luego las más recientes
                .OrderByDescending(b => Normalize(b.VersionName ?? string.Empty).StartsWith(normalizedQuery))
                .ThenByDescending(b => b.CreatedAt)
                .Take(MaxSuggestions);

            foreach (var budget in ranked)
            {
                Suggestions.Add(new BudgetSuggestion(budget));
            }
        }

        IsSuggestionsOpen = Suggestions.Count > 0;
    }

    private void NotifyResultsChanged()
    {
        OnPropertyChanged(nameof(HasSearchText));
        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(ResultSummary));
    }

    /// <summary>
    /// Indica si el presupuesto contiene todos los términos de búsqueda (sin distinguir mayúsculas ni tildes).
    /// </summary>
    private static bool Matches(Budget budget, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        string haystack = Normalize($"{budget.VersionName} {budget.DisplayName}");
        return query
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .All(term => haystack.Contains(Normalize(term)));
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }
}

/// <summary>
/// Elemento del autocompletado de estructuras: separa el código del nombre para mostrarlos con jerarquía visual.
/// </summary>
public sealed class BudgetSuggestion
{
    public BudgetSuggestion(Budget budget)
    {
        Budget = budget;
        Code = budget.VersionName ?? string.Empty;

        string display = budget.DisplayName ?? string.Empty;
        string prefix = $"{Code} - ";
        Name = display.StartsWith(prefix, StringComparison.Ordinal) ? display.Substring(prefix.Length) : display;
        DateText = budget.CreatedAt.ToString("dd/MM/yyyy");
    }

    public Budget Budget { get; }

    public string Code { get; }

    public string Name { get; }

    public string DateText { get; }
}
