namespace practicacq.Vistas;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    private static readonly Brush ColorA = Paleta.GrupoA;
    private static readonly Brush ColorB = Paleta.GrupoB;
    private static readonly Brush ColorNudo = new SolidColorBrush(Paleta.ConAlfa(Paleta.ColorNudo, 0x60));
    private static readonly Brush ColorZapata = new SolidColorBrush(Paleta.ConAlfa(Paleta.ColorZapata, 0x60));
    private static readonly Brush ColorContorno = Paleta.Contorno;
    private static readonly Brush ColorTexto = Paleta.TextoSecundario;
    private static readonly Brush ColorError = Paleta.Error;

    private const double MargenSuperior = 36;
    private const double MargenInferior = 20;
    /// <summary>Dónde va el primer plan, y cuánto se corre cada uno de los siguientes.</summary>
    private const double XPrimero = 125;
    private const double Separacion = 150;
    /// <summary>Dónde arranca la columna dibujada, y lo más ancha que puede quedar.</summary>
    private const double XColumna = 14;
    private const double AnchoMaximoColumna = 44;

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
        // El ancho de la columna a la misma escala que el alto (#133). No puede
        // pasar de AnchoMaximoColumna: a la derecha van las cotas del primer plan.
        double escala = EscalaDibujo.Ajustar(
            columna.BaseX, Math.Max(0.01, zMax - zMin), AnchoMaximoColumna, Lienzo.Height - MargenSuperior - MargenInferior);
        double anchoColumna = Math.Max(2, columna.BaseX * escala);
        double Y(double z) => MargenSuperior + (zMax - z) * escala;

        // Fondo: zapata y nudos de lado a lado, después el contorno de la columna.
        if (columna.ZInferiorLongitudinal < columna.ZInferior - 1e-6)
            Rectangulo(8, Y(columna.ZInferior), ancho, Y(zMin) - Y(columna.ZInferior) + 4, ColorZapata, null);
        foreach (Tramo nudo in columna.Tramos.Where(t => t.Tipo == TipoTramo.DentroDeViga))
            Rectangulo(8, Y(nudo.ZSuperior), ancho, Y(nudo.ZInferior) - Y(nudo.ZSuperior), ColorNudo, null);
        Rectangulo(XColumna, Y(zMax), anchoColumna, Y(columna.ZInferior) - Y(zMax), null, ColorContorno);
        Cotas.Horizontal(Lienzo, XColumna, XColumna + anchoColumna, Y(zMin) + 6, EscalaDibujo.Cm(columna.BaseX));

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
        TextoTitulo(titulo, tipo.DiametroMm, x - 45, 4, tipo.TieneSolucion ? color : ColorError);
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
            TextoVarilla($"{pieza.LargoCorte:0.00} m", tipo.DiametroMm, x + 9, (Y(pieza.ZDesde) + Y(pieza.ZHasta)) / 2 - 8, color);
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

    /// <summary>
    /// El largo de corte con el color del grupo (A o B) y, al lado, el número de
    /// varilla con su color de la tabla de la empresa (<see cref="ColoresVarilla"/>),
    /// el mismo que usan la vista previa y los filtros de Revit. Un diámetro que no
    /// está en la tabla sale como "Ø16" en gris.
    /// </summary>
    private void TextoVarilla(string largo, double diametroMm, double x, double y, Brush colorGrupo)
    {
        ColoresVarilla.ColorRgb c = ColoresVarilla.ColorDe(diametroMm);
        string etiqueta = ColoresVarilla.NumeroDe(diametroMm) is int numero ? $"#{numero}" : $"Ø{diametroMm:0}";

        var t = new TextBlock { FontSize = 10, Foreground = colorGrupo };
        t.Inlines.Add(new Run(largo));
        t.Inlines.Add(new Run($"  {etiqueta}")
        {
            Foreground = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)),
            FontWeight = FontWeights.Bold,
        });
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
        Lienzo.Children.Add(t);
    }

    /// <summary>
    /// "Tipo A · #5": el grupo con su color (A o B) y el "#5" con el de la tabla de
    /// la empresa, igual que el número junto al largo de cada varilla. Sin
    /// solución, todo va en el color de error.
    /// </summary>
    private void TextoTitulo(string titulo, double diametroMm, double x, double y, Brush colorGrupo)
    {
        var t = new TextBlock { FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = colorGrupo };
        int numero = titulo.IndexOf('#');
        if (numero < 0 || ReferenceEquals(colorGrupo, ColorError))
            t.Inlines.Add(new Run(titulo));
        else
        {
            ColoresVarilla.ColorRgb c = ColoresVarilla.ColorDe(diametroMm);
            t.Inlines.Add(new Run(titulo[..numero]));
            t.Inlines.Add(new Run(titulo[numero..]) { Foreground = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) });
        }

        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
        Lienzo.Children.Add(t);
    }

    private void AgregarNota(string texto, Brush color) =>
        Notas.Children.Add(new TextBlock { Text = texto, Foreground = color, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
}
