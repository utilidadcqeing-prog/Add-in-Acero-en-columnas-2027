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
        // Primera consulta al abrir; después, solo con «Buscar novedades».
        Loaded += async (_, _) => await vista.BuscarNovedades.EjecutarAsync();
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
