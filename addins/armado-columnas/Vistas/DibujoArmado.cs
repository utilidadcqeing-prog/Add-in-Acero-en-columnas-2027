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
}
