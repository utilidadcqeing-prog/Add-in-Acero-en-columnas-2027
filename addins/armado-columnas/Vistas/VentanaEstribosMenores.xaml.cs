namespace practicacq.Vistas;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using practicacq.Modelo;

/// <summary>
/// L4: "Configurador de Estribos Menores", rehecho el 20-oct para que sea
/// visual -- docs/planes/2026-10-20-diseno-estribos-menores-visual-y-64.md,
/// sección 1. Diálogo modal, se abre desde L2 con la cantidad de barras por
/// cara ya cargada. Sin una sola referencia a Revit, como el resto del carril.
///
/// La sección se ve desde arriba, igual que en el modelo: la fila 1 abajo y la
/// columna 1 a la izquierda -- el mismo orden que usa el generador
/// (<c>PosicionIntermedia</c> arranca en el mínimo de X y de Y). El panel de
/// casillas anterior dibujaba la fila 1 arriba, al revés que en Revit.
///
/// Cómo se pone cada pieza:
///  - Grapa en C / en S: un clic (o soltarla arrastrada) sobre una barra
///    intermedia; va siempre de borde a borde, hasta la barra de enfrente.
///  - Estribo cerrado: dos clics sobre barras intermedias del mismo eje.
///  - Mientras se arrastra o entre los dos clics, la pieza se ve
///    semitransparente donde quedaría.
/// Redibuja todo en cada cambio en vez de mantener controles sincronizados a
/// mano -- mismo criterio que la elevación de L2.
/// </summary>
public partial class VentanaEstribosMenores : Window
{
    private const double Lado = 260;
    private const double Margen = 40;

    /// <summary>A cuántos píxeles del centro de una barra se la considera "debajo del mouse".</summary>
    private const double RadioCaptura = 12;

    private const string IgualAlEstribo = "Igual al estribo";

    private static readonly Brush ColorGrapaC = Paleta.GrupoB;
    private static readonly Brush ColorGrapaS = Paleta.GrapaS;
    private static readonly Brush ColorCerrado = Paleta.Nudo;
    private static readonly Brush ColorSeleccion = Paleta.Seleccion;
    private static readonly Brush ColorTapada = Paleta.Contorno;
    private static readonly Brush ColorEstribo = Paleta.Estribo;
    private static readonly Brush ColorImposible = Paleta.Error;

    /// <summary>
    /// Lo que el panel necesita saber de la sección real para marcar en rojo un
    /// estribo cerrado que no se puede doblar (Luciano, 20-oct): la distancia,
    /// en metros, entre dos barras intermedias vecinas de cada eje, y cómo
    /// encontrar el diámetro y el doblado de una barra por su nombre.
    /// </summary>
    /// <param name="BarraEstribo">La barra de "Igual al estribo": la del tramo activo.</param>
    public sealed record MedidasSeccion(double PasoColumnas, double PasoFilas, Func<string?, TipoBarra?> Barra, string? BarraEstribo);

    private readonly MedidasSeccion? _medidas;

    private readonly int _filas;
    private readonly int _columnas;
    private readonly List<EstriboMenor> _piezas;

    /// <summary>La forma elegida en la paleta (o la que se está arrastrando), null si ninguna.</summary>
    private FormaMenor? _herramienta;

    /// <summary>Primer clic de un estribo cerrado a medio poner.</summary>
    private (EjeCaja Eje, int Indice)? _primerClic;

    private EstriboMenor? _seleccionada;
    private Point? _mouse;
    private bool _mostrandoPieza;
    private Point _inicioArrastre;

    /// <summary>Lo que queda configurado si el usuario acepta (DialogResult == true).</summary>
    public List<EstriboMenor> Piezas { get; private set; } = new();

    public VentanaEstribosMenores(
        int barrasPorCaraX, int barrasPorCaraY, IEnumerable<EstriboMenor> piezas, IReadOnlyList<string> barrasAptas,
        MedidasSeccion? medidas = null)
    {
        InitializeComponent();
        _medidas = medidas;

        _columnas = Math.Max(1, barrasPorCaraX / 2);
        _filas = Math.Max(1, barrasPorCaraY / 2);

        // Copias: Cancelar no puede dejar cambios a medias en la configuración.
        _piezas = piezas
            .Where(p => p.Desde >= 0 && p.Hasta < (p.Eje == EjeCaja.Fila ? _filas : _columnas))
            .Select(p => p.Copiar())
            .ToList();

        // Una barra que ya no está entre las aptas (configuración traída de otro
        // modelo) se muestra igual, para no cambiarla en silencio al abrir.
        ComboBarraPieza.ItemsSource = new[] { IgualAlEstribo }
            .Concat(barrasAptas)
            .Concat(_piezas.Select(p => p.TipoBarraNombre).OfType<string>().Except(barrasAptas))
            .Distinct()
            .ToList();

        TextoEncabezado.Text = $"X = {barrasPorCaraX} · Y = {barrasPorCaraY} (+ 4 esquinas). Vista desde arriba, como en el modelo.";

        DibujarIcono(IconoGrapaC, FormaMenor.GrapaC);
        DibujarIcono(IconoGrapaS, FormaMenor.GrapaS);
        DibujarIcono(IconoCerrado, FormaMenor.Cerrado);

        Dibujar();
    }

    // ----------------------------------------------------------------- geometría

    private static double Izquierda => Margen;
    private static double Derecha => Margen + Lado;
    private static double Arriba => Margen;
    private static double Abajo => Margen + Lado;

    /// <summary>Fila 0 abajo, como en el modelo; repartidas parejo entre las esquinas.</summary>
    private double Y(int fila) => Abajo - (fila + 1) * (Lado / (_filas + 1));

    /// <summary>Columna 0 a la izquierda.</summary>
    private double X(int columna) => Izquierda + (columna + 1) * (Lado / (_columnas + 1));

    /// <summary>
    /// La barra intermedia bajo el punto, si hay una: las de las caras izquierda
    /// y derecha son de eje <see cref="EjeCaja.Fila"/>, las de abajo y arriba de
    /// <see cref="EjeCaja.Columna"/>. Las esquinas no cuentan: una grapa ahí
    /// quedaría encima del estribo principal.
    /// </summary>
    private (EjeCaja Eje, int Indice)? BarraEn(Point p)
    {
        for (int f = 0; f < _filas; f++)
            foreach (double x in new[] { Izquierda, Derecha })
                if (Distancia(p, x, Y(f)) <= RadioCaptura) return (EjeCaja.Fila, f);

        for (int c = 0; c < _columnas; c++)
            foreach (double y in new[] { Arriba, Abajo })
                if (Distancia(p, X(c), y) <= RadioCaptura) return (EjeCaja.Columna, c);

        return null;
    }

    private static double Distancia(Point p, double x, double y) => Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));

    private bool DentroDeLaSeccion(Point p) =>
        p.X >= Izquierda - RadioCaptura && p.X <= Derecha + RadioCaptura &&
        p.Y >= Arriba - RadioCaptura && p.Y <= Abajo + RadioCaptura;

    /// <summary>
    /// La pieza que quedaría si se suelta o se hace clic en <paramref name="p"/>
    /// con la herramienta actual, o null si ahí no se puede poner.
    /// </summary>
    private EstriboMenor? PiezaPropuesta(Point p)
    {
        if (_herramienta is not FormaMenor forma || BarraEn(p) is not { } barra) return null;

        if (forma != FormaMenor.Cerrado)
        {
            // Una sola grapa por fila o columna, sea C o S.
            bool ocupada = _piezas.Any(o => o.Forma != FormaMenor.Cerrado && o.Eje == barra.Eje && o.Desde == barra.Indice);
            return ocupada ? null : new EstriboMenor { Forma = forma, Eje = barra.Eje, Desde = barra.Indice, Hasta = barra.Indice };
        }

        if (_primerClic is not { } primero || primero.Eje != barra.Eje || primero.Indice == barra.Indice) return null;

        return new EstriboMenor
        {
            Forma = FormaMenor.Cerrado,
            Eje = barra.Eje,
            Desde = Math.Min(primero.Indice, barra.Indice),
            Hasta = Math.Max(primero.Indice, barra.Indice),
        };
    }

    /// <summary>La pieza puesta bajo el punto: primero las grapas (son más finas), después el cerrado más chico.</summary>
    private EstriboMenor? PiezaEn(Point p)
    {
        const double tolerancia = 6;

        EstriboMenor? grapa = _piezas
            .Where(o => o.Forma != FormaMenor.Cerrado)
            .FirstOrDefault(o => o.Eje == EjeCaja.Fila
                ? Math.Abs(p.Y - Y(o.Desde)) <= tolerancia && p.X >= Izquierda - tolerancia && p.X <= Derecha + tolerancia
                : Math.Abs(p.X - X(o.Desde)) <= tolerancia && p.Y >= Arriba - tolerancia && p.Y <= Abajo + tolerancia);
        if (grapa is not null) return grapa;

        return _piezas
            .Where(o => o.Forma == FormaMenor.Cerrado)
            .Where(o =>
            {
                Rect r = RectanguloDe(o);
                return p.X >= r.Left - tolerancia && p.X <= r.Right + tolerancia && p.Y >= r.Top - tolerancia && p.Y <= r.Bottom + tolerancia;
            })
            .OrderBy(o => RectanguloDe(o).Width * RectanguloDe(o).Height)
            .FirstOrDefault();
    }

    /// <summary>El cerrado abarca su rango en su eje y cruza de extremo a extremo en el otro (como <see cref="CajaMenor"/>).</summary>
    private Rect RectanguloDe(EstriboMenor caja) => caja.Eje == EjeCaja.Fila
        ? new Rect(new Point(Izquierda + 6, Y(caja.Desde) + 6), new Point(Derecha - 6, Y(caja.Hasta) - 6))
        : new Rect(new Point(X(caja.Desde) - 6, Arriba + 6), new Point(X(caja.Hasta) + 6, Abajo - 6));

    // ------------------------------------------------------------------- dibujo

    private void Dibujar()
    {
        Lienzo.Children.Clear();
        Lienzo.Width = Margen * 2 + Lado;
        Lienzo.Height = Margen * 2 + Lado;

        // Sección y estribo principal.
        Agregar(new Rectangle { Width = Lado + 24, Height = Lado + 24, Fill = Paleta.Hormigon },
            Izquierda - 12, Arriba - 12);
        Agregar(new Rectangle { Width = Lado, Height = Lado, Stroke = ColorEstribo, StrokeThickness = 3 }, Izquierda, Arriba);

        // Los cerrados abajo de todo, para que no tapen a las grapas.
        foreach (EstriboMenor pieza in _piezas.OrderBy(p => p.Forma == FormaMenor.Cerrado ? 0 : 1))
            DibujarPieza(pieza, ColorDePieza(pieza), pieza == _seleccionada, opacidad: 1);

        // Vista previa semitransparente de lo que quedaría (en rojo si no se podría doblar).
        if (_mouse is Point mouse && PiezaPropuesta(mouse) is { } propuesta)
            DibujarPieza(propuesta, ColorDePieza(propuesta), seleccionada: false, opacidad: 0.45);

        // Barras: esquinas e intermedias, con la que está bajo el mouse resaltada.
        foreach ((double x, double y) in new[] { (Izquierda, Arriba), (Derecha, Arriba), (Izquierda, Abajo), (Derecha, Abajo) })
            Punto(x, y, 6, Paleta.Superficie);

        (EjeCaja Eje, int Indice)? bajoMouse = _mouse is Point m && _herramienta is not null ? BarraEn(m) : null;
        for (int f = 0; f < _filas; f++)
            foreach (double x in new[] { Izquierda, Derecha })
                PuntoIntermedio(x, Y(f), (EjeCaja.Fila, f), bajoMouse);
        for (int c = 0; c < _columnas; c++)
            foreach (double y in new[] { Arriba, Abajo })
                PuntoIntermedio(X(c), y, (EjeCaja.Columna, c), bajoMouse);

        // Rótulos de fila y columna, desde 1.
        for (int f = 0; f < _filas; f++) Texto($"F{f + 1}", Izquierda - 34, Y(f) - 8);
        for (int c = 0; c < _columnas; c++) Texto($"C{c + 1}", X(c) - 8, Abajo + 12);

        ActualizarPaleta();
        ActualizarPanelPieza();
        ActualizarNotas();
    }

    private void PuntoIntermedio(double x, double y, (EjeCaja, int) barra, (EjeCaja, int)? bajoMouse)
    {
        bool primero = _primerClic is { } p && p.Equals(barra);
        if (primero || Equals(bajoMouse, barra))
            Agregar(new Ellipse { Width = 22, Height = 22, Stroke = ColorDe(_herramienta ?? FormaMenor.Cerrado), StrokeThickness = 2 }, x - 11, y - 11);
        Punto(x, y, 5, Paleta.ErrorClaro);
    }

    private void DibujarPieza(EstriboMenor pieza, Brush color, bool seleccionada, double opacidad)
    {
        Brush trazo = seleccionada ? ColorSeleccion : color;
        double grosor = seleccionada ? 4 : 3;

        if (pieza.Forma == FormaMenor.Cerrado)
        {
            Rect r = RectanguloDe(pieza);
            var rect = new Rectangle
            {
                Width = r.Width, Height = r.Height, Stroke = trazo, StrokeThickness = grosor, Opacity = opacidad,
                Fill = new SolidColorBrush(Paleta.ConAlfa(Paleta.ColorNudo, 0x22)),
            };
            Agregar(rect, r.Left, r.Top);

            // El gancho de cierre, en la esquina de arranque (abajo a la izquierda).
            Lienzo.Children.Add(new Polyline
            {
                Points = new PointCollection { new(r.Left + 12, r.Bottom - 12), new(r.Left, r.Bottom) },
                Stroke = trazo, StrokeThickness = grosor, Opacity = opacidad,
            });
            if (seleccionada) Asas(r.TopLeft, r.BottomRight);
            return;
        }

        // C: los dos ganchos hacia el mismo lado; S: uno hacia cada lado.
        // "Voltear" invierte los dos.
        int lado1 = pieza.Volteado ? -1 : 1;
        int lado2 = pieza.Forma == FormaMenor.GrapaS ? -lado1 : lado1;

        PointCollection puntos = pieza.Eje == EjeCaja.Fila
            ? new PointCollection
            {
                new(Izquierda + 3, Y(pieza.Desde) + 10 * lado1), new(Izquierda - 5, Y(pieza.Desde) + 5 * lado1), new(Izquierda, Y(pieza.Desde)),
                new(Derecha, Y(pieza.Desde)), new(Derecha + 5, Y(pieza.Desde) + 5 * lado2), new(Derecha - 3, Y(pieza.Desde) + 10 * lado2),
            }
            : new PointCollection
            {
                new(X(pieza.Desde) + 10 * lado1, Abajo - 3), new(X(pieza.Desde) + 5 * lado1, Abajo + 5), new(X(pieza.Desde), Abajo),
                new(X(pieza.Desde), Arriba), new(X(pieza.Desde) + 5 * lado2, Arriba - 5), new(X(pieza.Desde) + 10 * lado2, Arriba + 3),
            };

        Lienzo.Children.Add(new Polyline { Points = puntos, Stroke = trazo, StrokeThickness = grosor, Opacity = opacidad });
        if (seleccionada) Asas(puntos[2], puntos[3]);
    }

    private void Asas(Point a, Point b)
    {
        foreach (Point p in new[] { a, b })
            Agregar(new Rectangle { Width = 9, Height = 9, Fill = ColorSeleccion }, p.X - 4.5, p.Y - 4.5);
    }

    private Brush ColorDePieza(EstriboMenor pieza) =>
        PaqueteEstribos.EstaTapada(pieza, _piezas) ? ColorTapada
        : Imposible(pieza) is not null ? ColorImposible
        : ColorDe(pieza.Forma);

    /// <summary>
    /// Por qué un estribo cerrado no se puede doblar con su barra -- más
    /// angosto que <see cref="EstriboMenor.AnchoMinimoCerradoMm"/> --, o null
    /// si se puede (o si no se conocen las medidas de la sección).
    /// </summary>
    private string? Imposible(EstriboMenor pieza)
    {
        if (pieza.Forma != FormaMenor.Cerrado || _medidas is null) return null;
        if (_medidas.Barra(pieza.TipoBarraNombre ?? _medidas.BarraEstribo) is not { } barra) return null;

        double paso = pieza.Eje == EjeCaja.Fila ? _medidas.PasoFilas : _medidas.PasoColumnas;
        double anchoMm = (pieza.Hasta - pieza.Desde) * paso * 1000;
        double minimoMm = EstriboMenor.AnchoMinimoCerradoMm(barra);
        if (anchoMm >= minimoMm) return null;

        var es = System.Globalization.CultureInfo.GetCultureInfo("es-CO");
        return string.Format(es,
            "El estribo cerrado de las {0} mide {1:0.0} cm entre ejes, y con «{2}» necesita al menos {3:0.0} cm para doblarse: " +
            "dibújalo entre barras más separadas o usa una barra más delgada.",
            Donde(pieza), anchoMm / 10, barra.Nombre, minimoMm / 10);
    }

    private static Brush ColorDe(FormaMenor forma) => forma switch
    {
        FormaMenor.GrapaC => ColorGrapaC,
        FormaMenor.GrapaS => ColorGrapaS,
        _ => ColorCerrado,
    };

    private static void DibujarIcono(Canvas lienzo, FormaMenor forma)
    {
        if (forma == FormaMenor.Cerrado)
        {
            var rect = new Rectangle { Width = 60, Height = 20, Stroke = Paleta.Texto, StrokeThickness = 2.5 };
            Canvas.SetLeft(rect, 15);
            Canvas.SetTop(rect, 3);
            lienzo.Children.Add(rect);
            lienzo.Children.Add(new Polyline
            {
                Points = new PointCollection { new(24, 14), new(15, 23) }, Stroke = Paleta.Texto, StrokeThickness = 2.5,
            });
            return;
        }

        int lado2 = forma == FormaMenor.GrapaS ? -1 : 1;
        lienzo.Children.Add(new Polyline
        {
            Points = new PointCollection { new(16, 21), new(10, 17), new(14, 13), new(76, 13), new(80, 13 + 4 * lado2), new(74, 13 + 8 * lado2) },
            Stroke = Paleta.Texto, StrokeThickness = 2.5,
        });
    }

    private void Agregar(UIElement el, double left, double top)
    {
        if (el is FrameworkElement fe) fe.IsHitTestVisible = false;
        Canvas.SetLeft(el, left);
        Canvas.SetTop(el, top);
        Lienzo.Children.Add(el);
    }

    private void Punto(double cx, double cy, double r, Brush relleno) =>
        Agregar(new Ellipse { Width = r * 2, Height = r * 2, Fill = relleno, Stroke = Paleta.ContornoOscuro, StrokeThickness = 1.2 },
            cx - r, cy - r);

    private void Texto(string texto, double x, double y) =>
        Agregar(new TextBlock { Text = texto, FontSize = 10, Foreground = Paleta.TextoSecundario }, x, y);

    // ------------------------------------------------------- paneles laterales

    private void ActualizarPaleta()
    {
        foreach ((Border borde, FormaMenor forma) in new[]
                 { (PaletaGrapaC, FormaMenor.GrapaC), (PaletaGrapaS, FormaMenor.GrapaS), (PaletaCerrado, FormaMenor.Cerrado) })
        {
            bool activa = _herramienta == forma;
            borde.BorderBrush = activa ? ColorSeleccion : Paleta.BordeSuave;
            borde.Background = activa ? Paleta.SeleccionFondo : Paleta.Superficie;
        }

        TextoAyuda.Text = _herramienta switch
        {
            FormaMenor.Cerrado when _primerClic is not null => "Haz clic en la otra barra del mismo eje. Esc cancela.",
            FormaMenor.Cerrado => "Haz clic en una barra intermedia y después en otra del mismo eje.",
            FormaMenor forma => $"Haz clic en una barra intermedia: la {Nombre(forma).ToLowerInvariant()} va de borde a borde. Esc cancela.",
            null => "Arrastra una forma sobre una barra, o elígela y haz clic en la barra.",
        };
    }

    private void ActualizarPanelPieza()
    {
        _mostrandoPieza = true;
        try
        {
            if (_seleccionada is not { } pieza)
            {
                TextoPieza.Text = "Ninguna. Haz clic en una pieza de la sección para seleccionarla.";
                PanelPieza.Visibility = Visibility.Collapsed;
                return;
            }

            TextoPieza.Text = Descripcion(pieza);
            PanelPieza.Visibility = Visibility.Visible;
            ComboBarraPieza.SelectedItem = pieza.TipoBarraNombre ?? IgualAlEstribo;
            BotonVoltear.IsEnabled = pieza.Forma != FormaMenor.Cerrado;
        }
        finally
        {
            _mostrandoPieza = false;
        }
    }

    private void ActualizarNotas()
    {
        NotaTapadas.Text = string.Join("\n", _piezas
            .Where(p => PaqueteEstribos.EstaTapada(p, _piezas))
            .Select(p => $"La {Nombre(p.Forma).ToLowerInvariant()} de la {Donde(p)} queda dentro de un estribo cerrado y no se va a generar.")
            .Concat(_piezas.Select(Imposible).OfType<string>()));

        int grapas = _piezas.Count(p => p.Forma != FormaMenor.Cerrado && !PaqueteEstribos.EstaTapada(p, _piezas));
        int cerrados = _piezas.Count(p => p.Forma == FormaMenor.Cerrado);
        TextoResumen.Text = $"{grapas} grapa(s), {cerrados} estribo(s) cerrado(s).";
    }

    private static string Nombre(FormaMenor forma) => forma switch
    {
        FormaMenor.GrapaC => "Grapa en C",
        FormaMenor.GrapaS => "Grapa en S",
        _ => "Estribo cerrado",
    };

    private static string Donde(EstriboMenor p)
    {
        string eje = p.Eje == EjeCaja.Fila ? "fila" : "columna";
        return p.Desde == p.Hasta ? $"{eje} {p.Desde + 1}" : $"{eje}s {p.Desde + 1} a {p.Hasta + 1}";
    }

    private static string Descripcion(EstriboMenor p) => $"{Nombre(p.Forma)}, {Donde(p)}";

    // ------------------------------------------------------------------ mouse

    private void Paleta_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string tag } || !Enum.TryParse(tag, out FormaMenor forma)) return;

        // Un clic sobre la forma ya elegida la suelta.
        _herramienta = _herramienta == forma ? null : forma;
        _primerClic = null;
        _inicioArrastre = e.GetPosition(this);
        Dibujar();
    }

    private void Paleta_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Border { Tag: string tag }) return;

        Vector movido = e.GetPosition(this) - _inicioArrastre;
        if (Math.Abs(movido.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(movido.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (!Enum.TryParse(tag, out FormaMenor forma)) return;
        _herramienta = forma;
        _primerClic = null;
        DragDrop.DoDragDrop((DependencyObject)sender, tag, DragDropEffects.Copy);

        _mouse = null;
        Dibujar();
    }

    private void Lienzo_DragOver(object sender, DragEventArgs e)
    {
        _mouse = e.GetPosition(Lienzo);
        e.Effects = BarraEn(_mouse.Value) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        Dibujar();
    }

    private void Lienzo_DragLeave(object sender, DragEventArgs e)
    {
        _mouse = null;
        Dibujar();
    }

    private void Lienzo_Drop(object sender, DragEventArgs e)
    {
        Poner(e.GetPosition(Lienzo));
        e.Handled = true;
    }

    private void Lienzo_MouseMove(object sender, MouseEventArgs e)
    {
        if (_herramienta is null) return;
        _mouse = e.GetPosition(Lienzo);
        Dibujar();
    }

    private void Lienzo_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_mouse is null) return;
        _mouse = null;
        Dibujar();
    }

    private void Lienzo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point p = e.GetPosition(Lienzo);

        if (_herramienta is not null)
        {
            if (!DentroDeLaSeccion(p)) Cancelar();
            else Poner(p);
            return;
        }

        _seleccionada = PiezaEn(p);
        Dibujar();
    }

    /// <summary>
    /// Clic o soltar sobre <paramref name="p"/> con la herramienta actual. Al
    /// terminar una pieza la herramienta se suelta y la pieza queda
    /// seleccionada, para elegirle la barra enseguida.
    /// </summary>
    private void Poner(Point p)
    {
        if (_herramienta is not FormaMenor forma) return;

        if (forma == FormaMenor.Cerrado && _primerClic is null)
        {
            if (BarraEn(p) is { } barra) _primerClic = barra;
            _mouse = p;
            Dibujar();
            return;
        }

        if (PiezaPropuesta(p) is not { } nueva)
        {
            // En un lugar donde no se puede: se sigue esperando.
            Dibujar();
            return;
        }

        _piezas.Add(nueva);
        _seleccionada = nueva;
        _herramienta = null;
        _primerClic = null;
        _mouse = null;
        Dibujar();
    }

    private void Cancelar()
    {
        _herramienta = null;
        _primerClic = null;
        _mouse = null;
        Dibujar();
    }

    private void Ventana_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (_herramienta is not null || _primerClic is not null))
        {
            Cancelar();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _seleccionada is not null && e.OriginalSource is not ComboBox)
        {
            Eliminar();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------- pieza seleccionada

    private void ComboBarraPieza_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_mostrandoPieza || _seleccionada is null || ComboBarraPieza.SelectedItem is not string elegida) return;
        _seleccionada.TipoBarraNombre = elegida == IgualAlEstribo ? null : elegida;
        Dibujar(); // otra barra puede hacer (o dejar de hacer) imposible un cerrado
    }

    private void BotonVoltear_Click(object sender, RoutedEventArgs e)
    {
        if (_seleccionada is null || _seleccionada.Forma == FormaMenor.Cerrado) return;
        _seleccionada.Volteado = !_seleccionada.Volteado;
        Dibujar();
    }

    private void BotonEliminar_Click(object sender, RoutedEventArgs e) => Eliminar();

    private void Eliminar()
    {
        if (_seleccionada is null) return;
        _piezas.Remove(_seleccionada);
        _seleccionada = null;
        Dibujar();
    }

    private void BotonAceptar_Click(object sender, RoutedEventArgs e)
    {
        // Las tapadas se guardan igual: si después se borra la caja, la grapa
        // reaparece sola. El "gana la caja" se resuelve al consumir esto.
        Piezas = _piezas;
        DialogResult = true;
    }

    private void BotonCancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
