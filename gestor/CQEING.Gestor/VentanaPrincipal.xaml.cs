using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CQEING.Gestor;

public partial class VentanaPrincipal : Window
{
    public VentanaPrincipal(VistaGestor vista)
    {
        InitializeComponent();
        DataContext = vista;
        // Primera consulta al abrir; después, solo con «Buscar novedades». La licencia se consulta a la par, sin frenar las tarjetas.
        Loaded += async (_, _) =>
        {
            Task licencia = vista.ConsultarLicenciaAsync();
            await vista.BuscarNovedades.EjecutarAsync();
            await licencia;
        };
    }

    /// <summary>El menú «⋯» se abre con clic o teclado; el ContextMenu no hereda el DataContext de la tarjeta por sí solo.</summary>
    private void AbrirVersiones_Click(object sender, RoutedEventArgs e)
    {
        var boton = (Button)sender;
        ContextMenu menu = boton.ContextMenu!;
        menu.DataContext = boton.DataContext;
        menu.PlacementTarget = boton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
