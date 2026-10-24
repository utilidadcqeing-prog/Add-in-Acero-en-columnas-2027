namespace practicacq.Vistas;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

/// <summary>
/// Un estribo o una grapa en planta (el eje que calcula
/// <see cref="Modelo.PlantaArmado"/>) como trazo de WPF, con el grosor de su
/// barra a escala: puntas y quiebres redondeados, como una barra real.
/// No captura el mouse: los lienzos resuelven los clics por su cuenta.
/// </summary>
internal static class DibujoArmado
{
    /// <param name="aPx">De metros en planta a píxeles del lienzo.</param>
    public static Polyline Trazo(
        IEnumerable<(double X, double Y)> eje, Func<double, double, Point> aPx, Brush color, double grosor, double opacidad = 1) =>
        new()
        {
            Points = new PointCollection(eje.Select(p => aPx(p.X, p.Y))),
            Stroke = color,
            StrokeThickness = grosor,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = opacidad,
            IsHitTestVisible = false,
        };

    /// <summary>
    /// Un choque (#131): la pieza rodeada en rojo, con el motivo al pasar el
    /// mouse. A diferencia del resto del dibujo, esta marca SÍ captura el
    /// mouse (para el ToolTip); el relleno transparente es lo que la hace
    /// sensible en todo el círculo y no solo en el borde.
    /// </summary>
    /// <param name="radioPx">El radio de la pieza en píxeles; la marca va 3 px por fuera.</param>
    public static Ellipse MarcaChoque(Point centro, double radioPx, string motivo)
    {
        double r = radioPx + 3;
        var marca = new Ellipse
        {
            Width = 2 * r,
            Height = 2 * r,
            Stroke = Paleta.Error,
            StrokeThickness = 1.8,
            StrokeDashArray = new DoubleCollection { 2, 1.5 },
            Fill = Brushes.Transparent,
            ToolTip = motivo,
        };
        System.Windows.Controls.Canvas.SetLeft(marca, centro.X - r);
        System.Windows.Controls.Canvas.SetTop(marca, centro.Y - r);
        return marca;
    }
}
