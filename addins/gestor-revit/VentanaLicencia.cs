using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CQEING.GestorRevit;

/// <summary>
/// Aviso de que este equipo no tiene licencia, con el motivo y el código del equipo, «Más tarde» y «Abrir el gestor».
/// Mismo estilo que <see cref="VentanaAviso"/>: en la esquina, no modal y sin robar el foco. No se cierra solo, porque sin
/// licencia los add-ins no están y el usuario tiene que saber por qué.
/// </summary>
internal sealed class VentanaLicencia : Window
{
    public const string Titulo = "Licencia de los add-ins CQEING";

    public const string Indicacion =
        "Abra el Gestor de add-ins y pulse «Solicitar acceso». Si ya lo pidió y se lo aprobaron, reinicie Revit.";

    private const double Margen = 16;

    public VentanaLicencia(string motivo)
    {
        Title = LanzadorGestor.Titulo;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = Contenido(motivo);
        Loaded += (_, _) => UbicarEnLaEsquina();
    }

    /// <summary>Muestra el aviso sobre Revit: se minimiza y se cierra con él.</summary>
    public void Mostrar(IntPtr ventanaRevit)
    {
        if (ventanaRevit != IntPtr.Zero) new WindowInteropHelper(this) { Owner = ventanaRevit };
        Show();
    }

    private void UbicarEnLaEsquina()
    {
        Rect area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - Margen;
        Top = area.Bottom - ActualHeight - Margen;
    }

    private StackPanel Contenido(string motivo)
    {
        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 12), MaxWidth = 380 };
        panel.Children.Add(new TextBlock { Text = Titulo, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(new TextBlock { Text = motivo, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = Indicacion,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 12),
        });

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        botones.Children.Add(VentanaAviso.Boton("Más tarde", Close));
        botones.Children.Add(VentanaAviso.Boton("Abrir el gestor", AbrirGestor));
        panel.Children.Add(botones);
        return panel;
    }

    private void AbrirGestor()
    {
        string? problema = LanzadorGestor.Abrir();
        Close();
        if (problema is not null)
            MessageBox.Show(problema, LanzadorGestor.Titulo, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
