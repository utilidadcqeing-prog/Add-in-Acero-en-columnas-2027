using System.Collections.Generic;
using System.Linq;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using CQEING_CuadroCantidades.UI.Utils;
using CQEING_CuadroCantidades.UI.ViewModels;

namespace CQEING_CuadroCantidades.UI.Views;

public partial class DynamicTableWindow : Window
{
    // Mientras se marcan en la tabla las filas que el usuario seleccionó en Revit
    private bool _isApplyingRevitSelection;

    public DynamicTableWindow(DynamicTableViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RevitSelectionChanged += ViewModel_RevitSelectionChanged;
        Closed += (_, _) =>
        {
            viewModel.RevitSelectionChanged -= ViewModel_RevitSelectionChanged;
            viewModel.Dispose();
        };
    }

    /// <summary>
    /// Marca en la tabla los elementos seleccionados en Revit y desplaza la tabla hasta el primero.
    /// </summary>
    private void ViewModel_RevitSelectionChanged(object? sender, IReadOnlyCollection<string> uniqueIds)
    {
        // Mientras el usuario trabaja en esta ventana, los cambios de selección en Revit los provoca ella misma
        // (filas o caras marcadas aquí): devolverlos a la tabla le quitaría su selección múltiple.
        if (IsActive)
            return;

        var selected = uniqueIds.ToHashSet();
        _isApplyingRevitSelection = true;
        try
        {
            ElementsDataGrid.UnselectAll();
            foreach (var item in ElementsDataGrid.Items)
            {
                if (item is DataRowView rowView && selected.Contains(rowView["RevitUniqueId"]?.ToString() ?? string.Empty))
                    ElementsDataGrid.SelectedItems.Add(item);
            }
        }
        finally
        {
            _isApplyingRevitSelection = false;
        }

        if (ElementsDataGrid.SelectedItems.Count > 0)
            ElementsDataGrid.ScrollIntoView(ElementsDataGrid.SelectedItems[0]);

        // Un solo aviso al modelo de vista con la selección completa, sin escribirla de vuelta en Revit
        if (DataContext is DynamicTableViewModel vm)
            vm.UpdateSelection(selected, syncRevit: false);
    }

    private void DataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        e.Column.CanUserSort = false;
        if (e.PropertyName == "RevitUniqueId")
        {
            e.Column.Visibility = System.Windows.Visibility.Collapsed;
        }
        else if (e.PropertyName == DoorWindowManualFields.OpeningColumn)
        {
            // Apertura: lista desplegable
            e.Column = new DataGridComboBoxColumn
            {
                Header = e.PropertyName,
                ItemsSource = DoorWindowManualFields.OpeningOptions,
                SelectedItemBinding = new System.Windows.Data.Binding($"[{e.PropertyName}]"),
            };
        }
        else if (e.Column is DataGridTextColumn textColumn)
        {
            textColumn.Binding = new System.Windows.Data.Binding($"[{e.PropertyName}]");
        }

        // Solo los campos manuales de puertas y ventanas se editan; lo demás viene de Revit
        if (!DoorWindowManualFields.IsEditableColumn(e.PropertyName))
        {
            e.Column.IsReadOnly = true;
        }
    }

    private void DataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        // La fila TOTAL no se edita
        if (e.Row.Item is DataRowView rowView
            && rowView.Row.Table.Columns.Contains("No.")
            && rowView["No."]?.ToString() == "TOTAL")
        {
            e.Cancel = true;
        }
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        string? columnName = e.Column.Header as string;
        if (e.EditAction != DataGridEditAction.Commit
            || !DoorWindowManualFields.IsEditableColumn(columnName)
            || e.Row.Item is not DataRowView rowView
            || DataContext is not DynamicTableViewModel vm)
            return;

        string? uniqueId = rowView["RevitUniqueId"]?.ToString();
        string value = e.EditingElement switch
        {
            TextBox textBox => textBox.Text,
            ComboBox comboBox => comboBox.SelectedItem as string ?? string.Empty,
            _ => string.Empty,
        };
        if (string.IsNullOrEmpty(uniqueId))
            return;

        // Se guarda cuando la grilla ya terminó de confirmar la celda y la fila
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new System.Action(async () => await vm.UpdateDoorWindowManualFieldAsync(uniqueId, columnName!, value)));
    }

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid dataGrid)
        {
            var uniqueIds = new List<string>();
            foreach (var item in dataGrid.SelectedItems)
            {
                if (item is DataRowView rowView)
                {
                    var uniqueId = rowView["RevitUniqueId"]?.ToString();
                    if (!string.IsNullOrEmpty(uniqueId))
                    {
                        uniqueIds.Add(uniqueId);
                    }
                }
            }

            // El modelo de vista actualiza el panel de caras y la selección en Revit
            if (!_isApplyingRevitSelection && DataContext is DynamicTableViewModel vm)
            {
                vm.UpdateSelection(uniqueIds);
            }
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        ElementsDataGrid.SelectAll();
    }

    private void ImageSelection_Click(object sender, RoutedEventArgs e)
    {
        var cb = sender as CheckBox;
        if (cb?.DataContext is Core.Domain.Entities.ActivityImageSnapshot snapshot && DataContext is DynamicTableViewModel vm)
        {
            if (cb.IsChecked == true)
            {
                int count = 0;
                foreach (var s in vm.Snapshots)
                {
                    if (s.IsSelectedForPublication) count++;
                }
                if (count > 3)
                {
                    MessageBox.Show("Puedes seleccionar un máximo de 3 imágenes para publicar.", "Límite alcanzado", MessageBoxButton.OK, MessageBoxImage.Warning);
                    cb.IsChecked = false;
                    snapshot.IsSelectedForPublication = false;
                    return;
                }
            }
            
            // Persistir la selección actualizando el JSON
            vm.UpdatePreviewImagesJson();
        }
    }
}
