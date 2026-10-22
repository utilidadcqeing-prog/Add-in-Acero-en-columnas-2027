namespace practicacq.Vistas;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

/// <summary>
/// Cotas de dibujo (línea, dos topes y el texto de la medida) para los
/// dibujos de la columna -- #133/#134: en todo lugar donde se ve la columna,
/// con sus medidas. Ningún elemento captura el mouse, para no tapar los clics
/// de los lienzos que se usan para elegir.
/// </summary>
internal static class Cotas
{
    private const double Tope = 3;
    private const double TamañoTexto = 9;

    /// <summary>De <paramref name="x1"/> a <paramref name="x2"/> a la altura <paramref name="y"/>, con el texto debajo.</summary>
    public static void Horizontal(Canvas lienzo, double x1, double x2, double y, string texto)
    {
        Linea(lienzo, x1, y, x2, y);
        Linea(lienzo, x1, y - Tope, x1, y + Tope);
        Linea(lienzo, x2, y - Tope, x2, y + Tope);

        TextBlock t = Texto(texto);
        Canvas.SetLeft(t, (x1 + x2) / 2 - t.DesiredSize.Width / 2);
        Canvas.SetTop(t, y + 1);
        lienzo.Children.Add(t);
    }

    /// <summary>De <paramref name="y1"/> a <paramref name="y2"/> en la <paramref name="x"/>, con el texto girado a la derecha.</summary>
    public static void Vertical(Canvas lienzo, double x, double y1, double y2, string texto)
    {
        Linea(lienzo, x, y1, x, y2);
        Linea(lienzo, x - Tope, y1, x + Tope, y1);
        Linea(lienzo, x - Tope, y2, x + Tope, y2);

        TextBlock t = Texto(texto);
        t.LayoutTransform = new RotateTransform(-90);
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(t, x + 1);
        Canvas.SetTop(t, (y1 + y2) / 2 - t.DesiredSize.Height / 2);
        lienzo.Children.Add(t);
    }

    private static TextBlock Texto(string texto)
    {
        var t = new TextBlock { Text = texto, FontSize = TamañoTexto, Foreground = Paleta.TextoSecundario, IsHitTestVisible = false };
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return t;
    }

    private static void Linea(Canvas lienzo, double x1, double y1, double x2, double y2) =>
        lienzo.Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Paleta.TextoSecundario, StrokeThickness = 0.8, IsHitTestVisible = false,
        });
}
