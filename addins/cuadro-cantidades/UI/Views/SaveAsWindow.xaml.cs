using System.Windows;
using CQEING_CuadroCantidades.UI.ViewModels;

namespace CQEING_CuadroCantidades.UI.Views;

public partial class SaveAsWindow : Window
{
    public SaveAsWindow(SaveAsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += () =>
        {
            DialogResult = viewModel.IsConfirmed;
            Close();
        };
    }
}
