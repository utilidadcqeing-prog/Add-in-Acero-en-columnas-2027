namespace practicacq.Tablero;

using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

/// <summary>
/// La barra de «Seleccionar en Revit» de la ventana de columnas, la misma que
/// la de vigas: va arriba del área de dibujo de Revit mientras el usuario
/// elige columnas con la selección normal de Revit, y dice cuántas lleva. No
/// usa <c>PickObjects</c>, así que Revit no muestra su Finalizar/Cancelar.
/// </summary>
internal sealed partial class BarraSeleccion : Window
{
    /// <param name="duenio">La ventana principal de Revit: la barra queda encima de ella.</param>
    /// <param name="areaDibujo">El área de dibujo de Revit en píxeles de pantalla (izquierda, arriba, derecha, abajo).</param>
    public BarraSeleccion(IntPtr duenio, (int Izquierda, int Arriba, int Derecha, int Abajo) areaDibujo)
    {
        InitializeComponent();
        new WindowInteropHelper(this).Owner = duenio;
        Mostrar(0);
        // Centrada arriba del área de dibujo; la posición de Revit viene en píxeles y WPF trabaja en DIP.
        Loaded += (_, _) =>
        {
            Matrix aDip = PresentationSource.FromVisual(this)?.CompositionTarget.TransformFromDevice ?? Matrix.Identity;
            Point arriba = aDip.Transform(new Point((areaDibujo.Izquierda + areaDibujo.Derecha) / 2.0, areaDibujo.Arriba));
            Left = arriba.X - ActualWidth / 2;
            Top = arriba.Y + 4;
        };
    }

    public event Action? Todas;

    public event Action? Terminar;

    public event Action? Cancelar;

    /// <summary>Cuántas columnas hay elegidas; sin ninguna, «Terminar» no se puede pulsar.</summary>
    public void Mostrar(int columnas)
    {
        Conteo.Text = columnas == 0
            ? "Ninguna columna elegida"
            : $"{columnas} {(columnas == 1 ? "columna elegida" : "columnas elegidas")}";
        BotonTerminar.IsEnabled = columnas > 0;
    }

    private void AlPulsarTodas(object sender, RoutedEventArgs e) => Todas?.Invoke();

    private void AlPulsarTerminar(object sender, RoutedEventArgs e) => Terminar?.Invoke();

    private void AlPulsarCancelar(object sender, RoutedEventArgs e) => Cancelar?.Invoke();
}
