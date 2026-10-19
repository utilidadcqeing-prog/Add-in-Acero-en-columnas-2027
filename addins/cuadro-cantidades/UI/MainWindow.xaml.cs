using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using CQEING_CuadroCantidades.UI.ViewModels;

namespace CQEING_CuadroCantidades.UI;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        SetWindowIcon();
        DataContext = viewModel;
        viewModel.ElementSelectedForScroll += ViewModel_ElementSelectedForScroll;
    }

    private void ViewModel_ElementSelectedForScroll(object? sender, object item)
    {
        Dispatcher.InvokeAsync(() =>
        {
            UnassignedElementsListView.ScrollIntoView(item);
            UnassignedElementsListView.Focus();
        });
    }

    private void SetWindowIcon()
    {
        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        string assemblyDirectory = Path.GetDirectoryName(assemblyPath) ?? AppContext.BaseDirectory;
        string iconPath = Path.Combine(assemblyDirectory, "Resources", "rubik.png");

        if (!File.Exists(iconPath))
        {
            return;
        }

        Icon = new BitmapImage(new Uri(iconPath));
    }

    private void ListView_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e
    )
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SyncSelectionToRevit();
        }
    }

    private void DataGrid_AutoGeneratingColumn(object sender, System.Windows.Controls.DataGridAutoGeneratingColumnEventArgs e)
    {
        e.Column.CanUserSort = false;
        if (e.PropertyName == "RevitUniqueId")
        {
            e.Column.Visibility = System.Windows.Visibility.Collapsed;
        }
        else if (e.Column is System.Windows.Controls.DataGridTextColumn textColumn)
        {
            textColumn.Binding = new System.Windows.Data.Binding($"[{e.PropertyName}]");
        }
    }
}
