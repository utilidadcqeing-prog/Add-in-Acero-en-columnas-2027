using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CQEING_CuadroCantidades.UI.ViewModels;

namespace CQEING_CuadroCantidades.UI.Views;

public partial class SelectStructureWindow : Window
{
    private readonly SelectStructureViewModel _viewModel;

    public SelectStructureWindow(SelectStructureViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.RequestClose += () =>
        {
            DialogResult = viewModel.IsConfirmed;
            Close();
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+F lleva el foco al buscador desde cualquier parte de la ventana
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    // El Popup es una ventana independiente: se cierra al mover o desactivar el diálogo para que no quede flotando
    private void Window_LocationChanged(object? sender, EventArgs e) => CloseSuggestions();

    private void Window_Deactivated(object? sender, EventArgs e) => CloseSuggestions();

    private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!SuggestionsPopup.IsKeyboardFocusWithin)
            CloseSuggestions();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (!_viewModel.IsSuggestionsOpen && _viewModel.Suggestions.Count > 0)
                    _viewModel.IsSuggestionsOpen = true;
                if (_viewModel.IsSuggestionsOpen)
                {
                    MoveHighlight(1);
                    e.Handled = true;
                }
                break;

            case Key.Up:
                if (_viewModel.IsSuggestionsOpen)
                {
                    MoveHighlight(-1);
                    e.Handled = true;
                }
                break;

            case Key.Enter:
                if (_viewModel.IsSuggestionsOpen)
                {
                    var suggestion =
                        SuggestionsList.SelectedItem as BudgetSuggestion ?? _viewModel.Suggestions.FirstOrDefault();
                    _viewModel.ApplySuggestionCommand.Execute(suggestion);
                }
                else if (_viewModel.ConfirmCommand.CanExecute(null))
                {
                    _viewModel.ConfirmCommand.Execute(null);
                }
                e.Handled = true;
                break;

            case Key.Escape:
                if (_viewModel.IsSuggestionsOpen)
                {
                    CloseSuggestions();
                    e.Handled = true;
                }
                else if (_viewModel.HasSearchText)
                {
                    _viewModel.ClearSearchCommand.Execute(null);
                    e.Handled = true;
                }
                break;
        }
    }

    private void SuggestionsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is BudgetSuggestion suggestion)
        {
            _viewModel.ApplySuggestionCommand.Execute(suggestion);
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }
    }

    private void BudgetsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BudgetsGrid.SelectedItem != null)
            BudgetsGrid.ScrollIntoView(BudgetsGrid.SelectedItem);
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SelectStructureViewModel viewModel)
        {
            if (viewModel.ConfirmCommand.CanExecute(null))
            {
                viewModel.ConfirmCommand.Execute(null);
            }
        }
    }

    /// <summary>
    /// Mueve el resaltado del autocompletado de forma circular.
    /// </summary>
    private void MoveHighlight(int delta)
    {
        int count = SuggestionsList.Items.Count;
        if (count == 0)
            return;

        int index = SuggestionsList.SelectedIndex + delta;
        if (index < 0)
            index = count - 1;
        else if (index >= count)
            index = 0;

        SuggestionsList.SelectedIndex = index;
        SuggestionsList.ScrollIntoView(SuggestionsList.SelectedItem);
    }

    private void CloseSuggestions()
    {
        if (_viewModel.IsSuggestionsOpen)
            _viewModel.IsSuggestionsOpen = false;
    }
}
