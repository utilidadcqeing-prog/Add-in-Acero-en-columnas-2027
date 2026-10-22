namespace practicacq.Vistas;

using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using practicacq.Modelo;

/// <summary>
/// #142: el cuadro de ganchos y traslapos de la empresa (<see cref="CuadroVarillas"/>),
/// de solo consulta, para ver antes de generar cómo deberían quedar las barras
/// sin medir en Revit. Sale del mismo cuadro con el que se genera, así que lo
/// que muestra es lo que queda en el modelo.
///
/// No es modal: el configurador la abre una sola vez, la deja al lado y le
/// pasa con <see cref="MarcarUsos"/> las varillas de la columna activa cada vez
/// que cambia una barra, para pintarlas en azul.
/// </summary>
public partial class VentanaCuadroVarillas : Window
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-CO");

    private static readonly string[] Encabezados =
        { "Varilla", "Ø mm", "Traslapo", "Gancho 90°\nE / D", "Gancho 180°\nE / D", "Estribo 135°\nE / D", "kg/m" };

    private static readonly SolidColorBrush FondoUsada = new(Paleta.ConAlfa(Paleta.ColorPrimario, 0x1F));

    public VentanaCuadroVarillas()
    {
        InitializeComponent();
        MarcarUsos(new Dictionary<int, IReadOnlyList<string>>());
    }

    /// <summary>Vuelve a armar la tabla con las varillas de <paramref name="usos"/> en azul y su uso al lado.</summary>
    public void MarcarUsos(IReadOnlyDictionary<int, IReadOnlyList<string>> usos)
    {
        Tabla.Children.Clear();
        Tabla.RowDefinitions.Clear();
        Tabla.ColumnDefinitions.Clear();
        foreach (string _ in Encabezados)
            Tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Tabla.RowDefinitions.Add(new RowDefinition());
        for (int c = 0; c < Encabezados.Length; c++)
            Poner(Celda(Encabezados[c], negrita: true, color: Paleta.TextoSecundario), 0, c, fondo: null, separador: Paleta.Borde);

        int fila = 1;
        foreach (CuadroVarillas.Varilla v in CuadroVarillas.Cuadro)
        {
            Tabla.RowDefinitions.Add(new RowDefinition());
            Brush? fondo = usos.ContainsKey(v.Numero) ? FondoUsada : null;

            var varilla = new StackPanel { Orientation = Orientation.Horizontal };
            varilla.Children.Add(Celda($"#{v.Numero}", negrita: true, margen: new Thickness(0)));
            if (usos.TryGetValue(v.Numero, out IReadOnlyList<string>? enQue))
                varilla.Children.Add(Celda(string.Join(", ", enQue), color: Paleta.Primario, tamano: 11,
                                           margen: new Thickness(6, 0, 0, 0)));
            varilla.Margin = new Thickness(8, 4, 8, 4);

            Poner(varilla, fila, 0, fondo, Paleta.Separador);
            Poner(Numero(v.DiametroMm.ToString("0.0", Es)), fila, 1, fondo, Paleta.Separador);
            Poner(Numero(v.Traslapo is { } t ? t.ToString("0.00", Es) : "—"), fila, 2, fondo, Paleta.Separador);
            Poner(Numero(Gancho(v.Gancho90Cuadro)), fila, 3, fondo, Paleta.Separador);
            Poner(Numero(Gancho(v.Gancho180Cuadro)), fila, 4, fondo, Paleta.Separador);
            Poner(Numero(v.GanchoEstribo135 is { } e ? Gancho(e) : "—"), fila, 5, fondo, Paleta.Separador);
            Poner(Numero(v.KgPorMetro.ToString("0.000", Es)), fila, 6, fondo, Paleta.Separador);
            fila++;
        }
    }

    private static string Gancho(CuadroVarillas.Gancho g) =>
        g.Extension.ToString("0.000", Es) + " / " + g.DiametroDoblado.ToString("0.000", Es);

    private static TextBlock Numero(string texto) => Celda(texto, monoespaciada: true);

    private static TextBlock Celda(string texto, bool negrita = false, bool monoespaciada = false,
                                   Brush? color = null, double tamano = 12, Thickness? margen = null) => new()
    {
        Text = texto,
        FontWeight = negrita ? FontWeights.SemiBold : FontWeights.Normal,
        FontFamily = monoespaciada ? new FontFamily("Consolas") : SystemFonts.MessageFontFamily,
        FontSize = tamano,
        Foreground = color ?? Paleta.Texto,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = margen ?? new Thickness(8, 4, 8, 4),
    };

    /// <summary>La celda va dentro de un borde para que el fondo azul de una fila usada sea continuo y lleve su línea de abajo.</summary>
    private void Poner(FrameworkElement contenido, int fila, int columna, Brush? fondo, Brush separador)
    {
        var celda = new Border
        {
            Child = contenido,
            Background = fondo ?? Brushes.Transparent,
            BorderBrush = separador,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        Grid.SetRow(celda, fila);
        Grid.SetColumn(celda, columna);
        Tabla.Children.Add(celda);
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();

    private void Ventana_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }
}
