namespace practicacq.Vistas;

using System.Collections.Generic;
using System.Linq;
using System.Windows;

/// <summary>
/// #131: antes de generar, la lista de choques de las columnas que se van a
/// armar. Avisa y deja seguir: "Generar igual" cierra con
/// <c>DialogResult = true</c>; "Volver a revisar" (o Esc) no genera.
/// Ver docs/planes/2026-10-22-avisos-de-choque-131-diseno.md.
/// </summary>
public partial class VentanaChoques : Window
{
    /// <param name="choques">Columna (su marca) y motivo, uno por choque.</param>
    public VentanaChoques(IReadOnlyList<(string Columna, string Motivo)> choques)
    {
        InitializeComponent();

        int columnas = choques.Select(c => c.Columna).Distinct().Count();
        Titulo.Text = $"Hay choques en {columnas} columna(s)";
        Lista.ItemsSource = choques.Select(c => $"• {c.Columna}: {c.Motivo}").ToList();
    }

    private void BotonRevisar_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void BotonGenerar_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
