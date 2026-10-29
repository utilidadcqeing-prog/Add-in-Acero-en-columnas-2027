using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CQEING.Gestor.Core;

namespace CQEING.GestorRevit;

/// <summary>
/// Aviso pequeño en la esquina inferior derecha: cuántas y cuáles versiones nuevas hay, con «Abrir el gestor» y «Más
/// tarde». Actualizar es obligatorio: los add-ins desactualizados no se dejan usar (BloqueoPorVersion). No es modal ni roba el foco, así que no interrumpe el trabajo, y se cierra solo (no mientras el ratón está encima).
/// </summary>
internal sealed class VentanaAviso : Window
{
    private static readonly TimeSpan Duracion = TimeSpan.FromSeconds(20);
    private const double Margen = 16;

    private readonly DispatcherTimer _reloj;

    public static string Titulo(int cantidad) =>
        cantidad == 1 ? "Hay 1 versión nueva de sus add-ins" : $"Hay {cantidad} versiones nuevas de sus add-ins";

    public static string Linea(Novedad n) => $"• {n.Nombre}: {n.Instalada} → {n.Nueva}";

    public VentanaAviso(IReadOnlyList<Novedad> novedades)
    {
        Title = LanzadorGestor.Titulo;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = Contenido(novedades);

        _reloj = new DispatcherTimer { Interval = Duracion };
        _reloj.Tick += (_, _) => Close();
        MouseEnter += (_, _) => _reloj.Stop();
        MouseLeave += (_, _) => _reloj.Start();
        Loaded += (_, _) => UbicarEnLaEsquina();
        Closed += (_, _) => _reloj.Stop();
    }

    /// <summary>Muestra el aviso sobre Revit (se minimiza y se cierra con él) y arranca la cuenta para cerrarse solo.</summary>
    public void Mostrar(IntPtr ventanaRevit)
    {
        if (ventanaRevit != IntPtr.Zero) new WindowInteropHelper(this) { Owner = ventanaRevit };
        Show();
        _reloj.Start();
    }

    private void UbicarEnLaEsquina()
    {
        Rect area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - Margen;
        Top = area.Bottom - ActualHeight - Margen;
    }

    private StackPanel Contenido(IReadOnlyList<Novedad> novedades)
    {
        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 12), MaxWidth = 380 };
        panel.Children.Add(new TextBlock { Text = Titulo(novedades.Count), FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) });
        foreach (Novedad novedad in novedades)
            panel.Children.Add(new TextBlock { Text = Linea(novedad), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) });
        bool gestor = novedades.Any(n => n.Id == EtiquetaRelease.IdGestor);
        panel.Children.Add(new TextBlock
        {
            Text = gestor
                ? "Actualizar es obligatorio: mientras el gestor no esté al día no se puede usar ningún add-in. Ábralo y pulse " +
                  "«Actualizar y reiniciar»; los add-ins vuelven a funcionar al reiniciar Revit."
                : "Actualizar es obligatorio: hasta hacerlo, estos add-ins no se pueden usar. Se actualizan desde el gestor, " +
                  "sin cerrar Revit, y vuelven a funcionar al reiniciar Revit.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 12),
        });

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        botones.Children.Add(Boton("Más tarde", Close));
        botones.Children.Add(Boton("Abrir el gestor", AbrirGestor));
        panel.Children.Add(botones);
        return panel;
    }

    /// <summary>Botón de los avisos del gestor en Revit (también lo usa <see cref="VentanaLicencia"/>).</summary>
    internal static Button Boton(string texto, Action accion)
    {
        var boton = new Button { Content = texto, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        boton.Click += (_, _) => accion();
        return boton;
    }

    private void AbrirGestor()
    {
        string? problema = LanzadorGestor.Abrir();
        Close();
        if (problema is not null)
            MessageBox.Show(problema, LanzadorGestor.Titulo, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
