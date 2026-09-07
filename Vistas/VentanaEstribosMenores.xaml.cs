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
/// L4: "Ventana Configurador de Estribos Menores". Diálogo modal, se abre
/// desde L2 con la cantidad de barras por cara ya cargada.
///
/// Dibuja el perímetro de la sección con sus 4 barras de esquina (fijas) y
/// sus barras intermedias -- una por fila (posición Y) a cada costado, una
/// por columna (posición X) arriba y abajo. Sin una sola referencia a Revit,
/// como el resto del carril.
///
/// Dos mecánicas de tildado distintas, no una grilla de celdas como la
/// versión anterior:
///  - Grapa: UNA casilla la define -- una por fila (a la izquierda, conecta
///    la barra intermedia izquierda con la derecha de borde a borde) o una
///    por columna (arriba, conecta la de arriba con la de abajo).
///  - Caja: DOS casillas -- fila (a la derecha) y columna (abajo). El cruce
///    entre TODAS las filas y columnas tildadas para caja es una caja cada
///    una (producto cruzado): no hace falta emparejarlas a mano, tildar 2
///    filas y 3 columnas da 6 cajas.
/// </summary>
public partial class VentanaEstribosMenores : Window
{
    private const double Lado = 220;
    private const double Margen = 56;

    private readonly int _filas;
    private readonly int _columnas;

    private readonly bool[] _grapaFila;
    private readonly bool[] _grapaColumna;
    private readonly bool[] _cajaFila;
    private readonly bool[] _cajaColumna;

    /// <summary>Lo que queda configurado si el usuario acepta (DialogResult == true).</summary>
    public List<int> GrapasFila { get; private set; } = new();
    public List<int> GrapasColumna { get; private set; } = new();
    public List<CajaMenor> Cajas { get; private set; } = new();

    public VentanaEstribosMenores(
        int barrasPorCaraX, int barrasPorCaraY,
        IEnumerable<int> grapasFila, IEnumerable<int> grapasColumna, IEnumerable<CajaMenor> cajas)
    {
        InitializeComponent();

        _columnas = Math.Max(1, barrasPorCaraX / 2);
        _filas = Math.Max(1, barrasPorCaraY / 2);

        _grapaFila = new bool[_filas];
        _grapaColumna = new bool[_columnas];
        _cajaFila = new bool[_filas];
        _cajaColumna = new bool[_columnas];

        foreach (int f in grapasFila)
            if (f >= 0 && f < _filas) _grapaFila[f] = true;

        foreach (int c in grapasColumna)
            if (c >= 0 && c < _columnas) _grapaColumna[c] = true;

        // Reconstruye el tildado a partir de los rangos fusionados guardados
        // -- marca ocupado cada índice que el rango cubre, no solo sus puntas.
        foreach (CajaMenor caja in cajas)
        {
            for (int f = Math.Max(0, caja.FilaDesde); f <= caja.FilaHasta && f < _filas; f++)
                _cajaFila[f] = true;
            for (int c = Math.Max(0, caja.ColumnaDesde); c <= caja.ColumnaHasta && c < _columnas; c++)
                _cajaColumna[c] = true;
        }

        TextoEncabezado.Text = $"X = {barrasPorCaraX} · Y = {barrasPorCaraY} (+ 4 esquinas, aparte)";

        Dibujar();
    }

    // Coordenadas del perímetro en el lienzo.
    private static double Izquierda => Margen;
    private static double Derecha => Margen + Lado;
    private static double Arriba => Margen;
    private static double Abajo => Margen + Lado;

    /// <summary>Posición Y de la fila -- repartidas parejo entre las dos esquinas, sin pisarlas.</summary>
    private double Y(int fila) => Arriba + (fila + 1) * (Lado / (_filas + 1));

    /// <summary>Posición X de la columna -- repartidas parejo entre las dos esquinas, sin pisarlas.</summary>
    private double X(int columna) => Izquierda + (columna + 1) * (Lado / (_columnas + 1));

    private void Dibujar()
    {
        Lienzo.Children.Clear();
        Lienzo.Width = Margen * 2 + Lado;
        Lienzo.Height = Margen * 2 + Lado;

        // 1) Perímetro de la sección.
        Agregar(new Rectangle
        {
            Width = Lado,
            Height = Lado,
            Stroke = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            StrokeThickness = 1.5,
        }, Izquierda, Arriba);

        // 2) Cajas: filas y columnas tildadas para caja, fusionadas por
        // contigüidad -- William confirmó (reunión 4-sep, 21:08-23:18) que
        // tildar varias pegadas da UN rectángulo grande ("toma el área más
        // grande"), no una caja por cada una; un salto corta la fusión.
        List<(int Desde, int Hasta)> rangosFilaCaja = Rangos(_cajaFila);
        List<(int Desde, int Hasta)> rangosColumnaCaja = Rangos(_cajaColumna);

        foreach ((int filaDesde, int filaHasta) in rangosFilaCaja)
            foreach ((int columnaDesde, int columnaHasta) in rangosColumnaCaja)
                DibujarCaja(filaDesde, filaHasta, columnaDesde, columnaHasta);

        // 3) Grapas tildadas: línea verde de borde a borde -- salvo que esa
        // fila/columna ya quede cubierta por una caja: ahí gana la caja, la
        // grapa no se dibuja ni se genera (confirmado por Luciano; mismo
        // criterio que aplica GeneradorLongitudinal.GenerarMenores).
        for (int f = 0; f < _filas; f++)
            if (_grapaFila[f] && !EnAlgunRango(rangosFilaCaja, f))
                LineaGrapa(Izquierda, Y(f), Derecha, Y(f));

        for (int c = 0; c < _columnas; c++)
            if (_grapaColumna[c] && !EnAlgunRango(rangosColumnaCaja, c))
                LineaGrapa(X(c), Arriba, X(c), Abajo);

        // 4) Esquinas -- siempre las 4, no se tildan.
        Punto(Izquierda, Arriba, esquina: true);
        Punto(Derecha, Arriba, esquina: true);
        Punto(Izquierda, Abajo, esquina: true);
        Punto(Derecha, Abajo, esquina: true);

        // 5) Filas: barra intermedia a cada costado + casilla de grapa
        //    (izquierda) y de caja (derecha).
        for (int f = 0; f < _filas; f++)
        {
            double y = Y(f);
            Punto(Izquierda, y, esquina: false);
            Punto(Derecha, y, esquina: false);

            int captura = f;
            Casilla(Izquierda - 34, y - 8, _grapaFila[f], () => _grapaFila[captura] = !_grapaFila[captura]);
            Casilla(Derecha + 14, y - 8, _cajaFila[f], () => _cajaFila[captura] = !_cajaFila[captura]);
        }

        // 6) Columnas: barra intermedia arriba y abajo + casilla de grapa
        //    (arriba) y de caja (abajo).
        for (int c = 0; c < _columnas; c++)
        {
            double x = X(c);
            Punto(x, Arriba, esquina: false);
            Punto(x, Abajo, esquina: false);

            int captura = c;
            Casilla(x - 8, Arriba - 34, _grapaColumna[c], () => _grapaColumna[captura] = !_grapaColumna[captura]);
            Casilla(x - 8, Abajo + 14, _cajaColumna[c], () => _cajaColumna[captura] = !_cajaColumna[captura]);
        }

        ActualizarResumen();
    }

    private void Agregar(UIElement el, double left, double top)
    {
        Canvas.SetLeft(el, left);
        Canvas.SetTop(el, top);
        Lienzo.Children.Add(el);
    }

    private void Punto(double cx, double cy, bool esquina)
    {
        double r = esquina ? 6 : 5;
        var el = new Ellipse
        {
            Width = r * 2,
            Height = r * 2,
            Fill = esquina ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
            StrokeThickness = esquina ? 1.5 : 1,
            IsHitTestVisible = false,
        };
        Agregar(el, cx - r, cy - r);
    }

    /// <summary>
    /// Redibuja todo en cada click en vez de mantener el estado del control
    /// sincronizado a mano -- mismo criterio que usa la elevación de L2
    /// (<c>VentanaConfiguradorEstribos.RedibujarElevacion</c>): más simple
    /// que dos fuentes de verdad (el array y el control) yendo cada una por
    /// su lado.
    /// </summary>
    private void Casilla(double left, double top, bool marcado, Action alternar)
    {
        var caja = new CheckBox { IsChecked = marcado, Width = 16, Height = 16 };
        caja.Click += (_, _) => { alternar(); Dibujar(); };
        Agregar(caja, left, top);
    }

    private void LineaGrapa(double x1, double y1, double x2, double y2)
    {
        Lienzo.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
            StrokeThickness = 3,
            IsHitTestVisible = false,
        });
    }

    /// <summary>Rectángulo real de <paramref name="filaDesde"/>/<paramref name="filaHasta"/> x <paramref name="columnaDesde"/>/<paramref name="columnaHasta"/> -- ya no un marcador puntual.</summary>
    private void DibujarCaja(int filaDesde, int filaHasta, int columnaDesde, int columnaHasta)
    {
        double x1 = X(columnaDesde);
        double x2 = X(columnaHasta);
        double y1 = Y(filaDesde);
        double y2 = Y(filaHasta);

        var rect = new Rectangle
        {
            Width = Math.Abs(x2 - x1),
            Height = Math.Abs(y2 - y1),
            Fill = new SolidColorBrush(Color.FromArgb(0x40, 0x3B, 0x82, 0xF6)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            StrokeThickness = 2,
            IsHitTestVisible = false,
        };
        Agregar(rect, Math.Min(x1, x2), Math.Min(y1, y2));
    }

    /// <summary>
    /// Fusiona índices tildados consecutivos en rangos -- confirmado por
    /// William (reunión 4-sep, 21:08: "tomaría el área más grande"; 22:44-23:18:
    /// tres tildadas pegadas dan un solo rectángulo grande, "porque están
    /// juntos... no se pueden crear dos rectángulos pegaditos"). Un índice sin
    /// tildar en el medio corta la fusión y arranca un rango nuevo.
    /// </summary>
    private static List<(int Desde, int Hasta)> Rangos(bool[] tildado)
    {
        var rangos = new List<(int Desde, int Hasta)>();
        int? inicio = null;

        for (int i = 0; i < tildado.Length; i++)
        {
            if (tildado[i])
            {
                inicio ??= i;
            }
            else if (inicio is not null)
            {
                rangos.Add((inicio.Value, i - 1));
                inicio = null;
            }
        }

        if (inicio is not null) rangos.Add((inicio.Value, tildado.Length - 1));
        return rangos;
    }

    private static bool EnAlgunRango(List<(int Desde, int Hasta)> rangos, int indice) =>
        rangos.Any(r => indice >= r.Desde && indice <= r.Hasta);

    private void ActualizarResumen()
    {
        List<(int Desde, int Hasta)> rangosFilaCaja = Rangos(_cajaFila);
        List<(int Desde, int Hasta)> rangosColumnaCaja = Rangos(_cajaColumna);

        int grapas =
            Enumerable.Range(0, _filas).Count(f => _grapaFila[f] && !EnAlgunRango(rangosFilaCaja, f)) +
            Enumerable.Range(0, _columnas).Count(c => _grapaColumna[c] && !EnAlgunRango(rangosColumnaCaja, c));
        int cajas = rangosFilaCaja.Count * rangosColumnaCaja.Count;

        TextoResumen.Text = $"{grapas} grapa(s), {cajas} caja(s).";
    }

    private void BotonAceptar_Click(object sender, RoutedEventArgs e)
    {
        // GrapasFila/GrapasColumna guardan el tildado crudo, sin filtrar por
        // las cajas que lo tapan -- así, si más adelante se destilda esa
        // caja, la grapa reaparece sola sin que haya que volver a tildarla.
        // El "gana la caja" se resuelve al CONSUMIR esto (acá en el dibujo,
        // y en GeneradorLongitudinal.GenerarMenores), no acá al guardar.
        GrapasFila = Enumerable.Range(0, _filas).Where(f => _grapaFila[f]).ToList();
        GrapasColumna = Enumerable.Range(0, _columnas).Where(c => _grapaColumna[c]).ToList();

        List<(int Desde, int Hasta)> rangosFila = Rangos(_cajaFila);
        List<(int Desde, int Hasta)> rangosColumna = Rangos(_cajaColumna);

        Cajas = new List<CajaMenor>();
        foreach ((int filaDesde, int filaHasta) in rangosFila)
            foreach ((int columnaDesde, int columnaHasta) in rangosColumna)
                Cajas.Add(new CajaMenor
                {
                    FilaDesde = filaDesde,
                    FilaHasta = filaHasta,
                    ColumnaDesde = columnaDesde,
                    ColumnaHasta = columnaHasta,
                });

        DialogResult = true;
    }

    private void BotonCancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
