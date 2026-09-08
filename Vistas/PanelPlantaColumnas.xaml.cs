namespace practicacq.Vistas;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using practicacq.Modelo;

/// <summary>
/// Selector visual de columnas en planta (reunión 9-sep): William pidió ver
/// las columnas ubicadas como en un plano de planta real -- con las rejillas
/// y sus burbujas de nombre si el modelo las tiene, norte de proyecto
/// siempre hacia arriba, y que si no hay rejillas (o ninguna vista de planta
/// abierta en Revit) se vea igual, solo sin ellas -- en vez de la lista de
/// texto sola, para identificar más rápido cuál columna es cuál antes de
/// elegirla.
///
/// Complementa la lista de <see cref="VentanaConfiguradorEstribos"/> (que
/// sigue siendo la única forma de tildar TRAMOS sueltos), no la reemplaza:
/// un clic acá activa Y tilda la columna entera (todos sus tramos), mismo
/// gesto combinado que tildar la fila en negrita de la lista -- un segundo
/// clic destilda. El dibujo
/// es enteramente propio (un <see cref="Canvas"/> armado a partir de
/// <see cref="ColumnaResumen.X"/>/<see cref="ColumnaResumen.Y"/> y
/// <see cref="Rejilla"/>), no depende de que exista una vista de planta de
/// Revit ni de que el modelo tenga rejillas -- ninguna de las dos cosas es
/// requisito para que esto dibuje algo útil.
///
/// Norte: se usan directamente las coordenadas de proyecto (X/Y), sin
/// transformar por Norte Verdadero -- es lo mismo que muestra cualquier
/// vista de planta por defecto de Revit. Simplificación deliberada (no hay
/// manejo de <c>ProjectLocation</c>/Norte Verdadero en todo el repo).
///
/// Mismo criterio que <c>RedibujarElevacion</c> (<see cref="VentanaConfiguradorEstribos"/>)
/// y <c>Dibujar</c> (<see cref="VentanaEstribosMenores"/>): redibuja todo
/// desde cero en cada llamada a <see cref="Cargar"/>, no actualiza
/// incrementalmente.
/// </summary>
public partial class PanelPlantaColumnas : UserControl
{
    /// <summary>Se dispara con el <see cref="ColumnaResumen.Id"/> de la columna clickeada.</summary>
    public event Action<string>? ColumnaClicked;

    private static readonly Brush ColorColumna = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
    private static readonly Brush ColorBordeColumna = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));
    private static readonly Brush ColorColumnaActiva = new SolidColorBrush(Color.FromArgb(120, 0x22, 0xC5, 0x5E));
    private static readonly Brush ColorBordeActiva = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));

    // Mismo azul que BotonAplicarSeleccion (VentanaConfiguradorEstribos.xaml)
    // -- una columna tildada (incluida en "Aplicar a selección") se marca con
    // este color, independiente de si además está activa (verde).
    private static readonly Brush ColorColumnaMarcada = new SolidColorBrush(Color.FromArgb(140, 0x0B, 0x63, 0xCE));
    private static readonly Brush ColorBordeMarcada = new SolidColorBrush(Color.FromRgb(0x0B, 0x63, 0xCE));

    private static readonly Brush ColorRejilla = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));

    public PanelPlantaColumnas() => InitializeComponent();

    /// <summary>
    /// Redibuja todo el panel. <paramref name="idActiva"/> es el
    /// <see cref="ColumnaResumen.Id"/> de la columna activa (para resaltarla
    /// en verde), o <c>null</c> si ninguna lo está todavía.
    /// <paramref name="idsMarcadas"/> son las columnas tildadas (incluidas en
    /// "Aplicar a selección" -- se resaltan en azul, ver
    /// <see cref="ColumnaClicked"/>).
    /// </summary>
    public void Cargar(
        IReadOnlyList<ColumnaResumen> columnas, IReadOnlyList<Rejilla> rejillas,
        string? idActiva, IReadOnlySet<string> idsMarcadas)
    {
        Lienzo.Children.Clear();
        if (columnas.Count == 0 && rejillas.Count == 0) return;

        const double margen = 22;
        double anchoDisponible = Math.Max(1, Lienzo.Width - margen * 2);
        double altoDisponible = Math.Max(1, Lienzo.Height - margen * 2);

        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        void Expandir(double x, double y)
        {
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        foreach (ColumnaResumen c in columnas) Expandir(c.X, c.Y);
        foreach (Rejilla r in rejillas)
        {
            Expandir(r.X1, r.Y1);
            Expandir(r.X2, r.Y2);
        }

        // Medio metro de rango mínimo -- evita una escala absurda (o una
        // división por casi cero) cuando solo hay una columna sola, o varias
        // exactamente en el mismo punto (proveedor falso, columnas sin
        // posición real).
        double rangoX = Math.Max(0.5, maxX - minX);
        double rangoY = Math.Max(0.5, maxY - minY);
        double escala = Math.Min(anchoDisponible / rangoX, altoDisponible / rangoY);

        double centroX = (minX + maxX) / 2;
        double centroY = (minY + maxY) / 2;
        double cx0 = margen + anchoDisponible / 2;
        double cy0 = margen + altoDisponible / 2;

        // Revit: Y crece "hacia el norte" (arriba). Canvas: Y crece hacia
        // abajo -- se invierte acá, una sola vez, para que "norte arriba" no
        // dependa de acordarse de invertir en cada dibujo.
        double Cx(double x) => cx0 + (x - centroX) * escala;
        double Cy(double y) => cy0 - (y - centroY) * escala;

        DibujarIndicadorNorte();

        foreach (Rejilla r in rejillas)
            DibujarRejilla(r, Cx, Cy);

        foreach (ColumnaResumen c in columnas)
            DibujarColumna(c, Cx(c.X), Cy(c.Y), c.Id == idActiva, idsMarcadas.Contains(c.Id));
    }

    private void DibujarIndicadorNorte()
    {
        var texto = new TextBlock
        {
            Text = "N ↑",
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = Brushes.Gray,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(texto, 4);
        Canvas.SetTop(texto, 2);
        Lienzo.Children.Add(texto);
    }

    private void DibujarRejilla(Rejilla r, Func<double, double> cx, Func<double, double> cy)
    {
        var linea = new Line
        {
            X1 = cx(r.X1),
            Y1 = cy(r.Y1),
            X2 = cx(r.X2),
            Y2 = cy(r.Y2),
            Stroke = ColorRejilla,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false,
        };
        Lienzo.Children.Add(linea);

        const double radioBurbuja = 9;
        double bx = linea.X1;
        double by = linea.Y1;

        var circulo = new Ellipse
        {
            Width = radioBurbuja * 2,
            Height = radioBurbuja * 2,
            Stroke = Brushes.DimGray,
            StrokeThickness = 1,
            Fill = Brushes.White,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(circulo, bx - radioBurbuja);
        Canvas.SetTop(circulo, by - radioBurbuja);
        Lienzo.Children.Add(circulo);

        var etiqueta = new TextBlock
        {
            Text = r.Nombre,
            FontSize = 9,
            TextAlignment = TextAlignment.Center,
            Width = radioBurbuja * 2,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(etiqueta, bx - radioBurbuja);
        Canvas.SetTop(etiqueta, by - 6);
        Lienzo.Children.Add(etiqueta);
    }

    private void DibujarColumna(ColumnaResumen c, double x, double y, bool activa, bool marcada)
    {
        const double tam = 10;

        var marcador = new Rectangle
        {
            Width = tam,
            Height = tam,
            Fill = marcada ? ColorColumnaMarcada : (activa ? ColorColumnaActiva : ColorColumna),
            Stroke = activa ? ColorBordeActiva : (marcada ? ColorBordeMarcada : ColorBordeColumna),
            StrokeThickness = activa || marcada ? 2 : 1,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        Canvas.SetLeft(marcador, x - tam / 2);
        Canvas.SetTop(marcador, y - tam / 2);

        string idCapturado = c.Id;
        marcador.MouseLeftButtonUp += (_, _) => ColumnaClicked?.Invoke(idCapturado);
        Lienzo.Children.Add(marcador);

        var etiqueta = new TextBlock
        {
            Text = c.Marca,
            FontSize = 9,
            Foreground = Brushes.Black,
            FontWeight = activa ? FontWeights.Bold : FontWeights.Normal,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(etiqueta, x + tam / 2 + 2);
        Canvas.SetTop(etiqueta, y - 7);
        Lienzo.Children.Add(etiqueta);
    }
}
