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
/// Dibuja lo que calculó <see cref="PlanTraslapos"/> para la columna activa:
/// la columna a escala con sus nudos (y la zapata, si la hay), y al lado una
/// barra por cada plan -- A y B, y dentro de cada uno, un dibujo por número
/// de barra (esquinas #5 y caras #4 dan "A · #5", "A · #4", etc.) --, cada
/// varilla con su largo de corte y cada traslapo con su largo y sus cotas. Un tipo sin solución sale en rojo con
/// el motivo debajo -- docs/planes/2026-10-17-diseno-metodologia-traslapos.md,
/// sección 8.
///
/// Las varillas consecutivas se dibujan corridas unos píxeles a un lado y al
/// otro, para que el traslapo se vea como dos líneas paralelas.
/// </summary>
public partial class PanelTraslapos : UserControl
{
    private static readonly Brush ColorA = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));
    private static readonly Brush ColorB = new SolidColorBrush(Color.FromRgb(0x04, 0x78, 0x57));
    private static readonly Brush ColorNudo = new SolidColorBrush(Color.FromArgb(0x60, 0x3B, 0x82, 0xF6));
    private static readonly Brush ColorZapata = new SolidColorBrush(Color.FromArgb(0x60, 0xB4, 0x53, 0x09));
    private static readonly Brush ColorContorno = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
    private static readonly Brush ColorTexto = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly Brush ColorError = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));

    private const double MargenSuperior = 36;
    private const double MargenInferior = 20;
    /// <summary>Dónde va el primer plan, y cuánto se corre cada uno de los siguientes.</summary>
    private const double XPrimero = 125;
    private const double Separacion = 130;

    public PanelTraslapos() => InitializeComponent();

    /// <summary>Sin dibujo: solo un texto (por ejemplo, "Elige la barra A y la barra B").</summary>
    public void MostrarMensaje(string mensaje)
    {
        Lienzo.Children.Clear();
        Notas.Children.Clear();
        AgregarNota(mensaje, ColorTexto);
    }

    public void Mostrar(ColumnaContexto columna, PlanTraslapos.Resultado plan)
    {
        Lienzo.Children.Clear();
        Notas.Children.Clear();

        List<PlanTraslapos.PlanTipo> planes = plan.Todos.ToList();
        Lienzo.Width = XPrimero + Separacion * Math.Max(0, planes.Count - 1) + 95;
        double ancho = Lienzo.Width - 16;

        double zMin = Math.Min(columna.ZInferiorLongitudinal, columna.ZInferior);
        double zMax = columna.ZSuperior;
        double escala = Math.Max(1, Lienzo.Height - MargenSuperior - MargenInferior) / Math.Max(0.01, zMax - zMin);
        double Y(double z) => MargenSuperior + (zMax - z) * escala;

        // Fondo: zapata y nudos de lado a lado, después el contorno de la columna.
        if (columna.ZInferiorLongitudinal < columna.ZInferior - 1e-6)
            Rectangulo(8, Y(columna.ZInferior), ancho, Y(zMin) - Y(columna.ZInferior) + 4, ColorZapata, null);
        foreach (Tramo nudo in columna.Tramos.Where(t => t.Tipo == TipoTramo.DentroDeViga))
            Rectangulo(8, Y(nudo.ZSuperior), ancho, Y(nudo.ZInferior) - Y(nudo.ZSuperior), ColorNudo, null);
        Rectangulo(20, Y(zMax), 26, Y(columna.ZInferior) - Y(zMax), null, ColorContorno);

        for (int i = 0; i < planes.Count; i++)
            DibujarTipo(planes[i], $"Tipo {planes[i].Nombre}", XPrimero + i * Separacion, planes[i].Grupo == "A" ? ColorA : ColorB, Y);

        foreach (PlanTraslapos.PlanTipo tipo in planes)
        {
            if (!tipo.TieneSolucion)
                // El motivo ya empieza con "tipo A: ...": solo se le pone mayúscula.
                AgregarNota($"{char.ToUpper(tipo.Motivo![0])}{tipo.Motivo[1..]} Esta columna no va a generar su acero longitudinal.", ColorError);
            else if (tipo.EnTramosDelOtro && tipo.Traslapos.Count > 0)
                AgregarNota($"El tipo {tipo.Nombre} no tiene tramos propios: traslapa en los del otro tipo, a otra altura.", ColorTexto);
        }

        List<string> solapes = planes
            .Where(p => p.SolapeMinimo is not null)
            .Select(p => $"{p.Nombre} {p.SolapeMinimo:0.00} m")
            .ToList();
        if (solapes.Count > 0)
            AgregarNota($"Solape mínimo: {string.Join(" · ", solapes)}. Los traslapos pueden ser más largos por el redondeo.", ColorTexto);
    }

    private void DibujarTipo(PlanTraslapos.PlanTipo tipo, string titulo, double x, Brush color, Func<double, double> Y)
    {
        Texto(titulo, x - 45, 4, tipo.TieneSolucion ? color : ColorError, negrita: true);
        if (!tipo.TieneSolucion)
        {
            Texto("sin solución", x - 30, 20, ColorError);
            return;
        }

        for (int i = 0; i < tipo.Piezas.Count; i++)
        {
            PlanTraslapos.Pieza pieza = tipo.Piezas[i];
            double xi = x + (i % 2 == 0 ? -3 : 3);
            Linea(xi, Y(pieza.ZDesde), xi, Y(pieza.ZHasta), color, 3);
            Texto($"{pieza.LargoCorte:0.00} m", x + 9, (Y(pieza.ZDesde) + Y(pieza.ZHasta)) / 2 - 8, color);
        }

        foreach (PlanTraslapos.Traslapo t in tipo.Traslapos)
        {
            Linea(x - 10, Y(t.ZSuperior), x - 10, Y(t.ZInferior), ColorTexto, 1);
            Texto($"{t.Largo:0.00} m", x - 62, Y(t.ZSuperior) - 2, ColorTexto);
            Texto($"{t.ZInferior:0.00}–{t.ZSuperior:0.00}", x - 62, Y(t.ZSuperior) + 10, ColorTexto, chico: true);
        }
    }

    private void Rectangulo(double x, double y, double ancho, double alto, Brush? relleno, Brush? borde)
    {
        var r = new Rectangle { Width = Math.Max(1, ancho), Height = Math.Max(1, alto), Fill = relleno, Stroke = borde };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        Lienzo.Children.Add(r);
    }

    private void Linea(double x1, double y1, double x2, double y2, Brush color, double grosor) =>
        Lienzo.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = color, StrokeThickness = grosor });

    private void Texto(string texto, double x, double y, Brush color, bool negrita = false, bool chico = false)
    {
        var t = new TextBlock
        {
            Text = texto, Foreground = color, FontSize = chico ? 9 : 10,
            FontWeight = negrita ? FontWeights.SemiBold : FontWeights.Normal,
        };
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
        Lienzo.Children.Add(t);
    }

    private void AgregarNota(string texto, Brush color) =>
        Notas.Children.Add(new TextBlock { Text = texto, Foreground = color, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
}
