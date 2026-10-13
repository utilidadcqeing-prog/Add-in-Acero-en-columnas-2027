using System.Windows;

namespace practicacq.Imagenes.Vistas;

/// <summary>
/// Barra flotante del paso "Ir a Revit": mientras está abierta, la ventana principal se esconde y el
/// usuario selecciona en Revit con las herramientas de siempre. Solo avisa lo que pulsa el usuario;
/// quien habla con Revit es <see cref="VentanaImagenesActividad"/>.
/// </summary>
public partial class BarraSeleccion : Window
{
    private bool _respondida;

    public BarraSeleccion(bool mismoTipo)
    {
        InitializeComponent();
        ChkMismoTipo.IsChecked = mismoTipo;
        Loaded += (_, _) =>
        {
            // Arriba al centro de la pantalla, donde no tapa el modelo.
            Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - ActualWidth) / 2;
            Top = SystemParameters.WorkArea.Top + 70;
        };
        // Cerrar con la X equivale a Cancelar.
        Closing += (_, _) =>
        {
            if (!_respondida) Cancelada?.Invoke();
        };
    }

    public event Action? Aceptada;
    public event Action? Cancelada;
    public event Action<bool>? MismoTipoCambiado;

    public bool MismoTipo => ChkMismoTipo.IsChecked == true;

    public void MostrarCantidad(int cantidad)
    {
        TxtCantidad.Text = cantidad == 1 ? "1 seleccionado" : $"{cantidad} seleccionados";
        BtnAceptar.IsEnabled = cantidad > 0;
    }

    private void ChkMismoTipo_Cambio(object sender, RoutedEventArgs e) => MismoTipoCambiado?.Invoke(MismoTipo);

    private void BtnAceptar_Click(object sender, RoutedEventArgs e)
    {
        _respondida = true;
        Aceptada?.Invoke();
        Close();
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        _respondida = true;
        Cancelada?.Invoke();
        Close();
    }
}
