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
    /// <summary>Lo más que ocupa el lado mayor de la sección, en píxeles: el otro sale de la proporción real (#133).</summary>
    private const double CajaDibujo = 300;
    private const double MargenIzquierdo = 40;
    private const double MargenArriba = 16;
    private const double MargenDerecho = 40;
    private const double MargenAbajo = 44;

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
    /// estribo cerrado que no se puede doblar (Luciano, 20-oct): cómo encontrar
    /// el diámetro y el doblado de una barra por su nombre. Las medidas del
    /// cerrado salen del mismo dibujo (<see cref="Imposible"/>).
    /// </summary>
    /// <param name="BarraEstribo">La barra de "Igual al estribo": la del tramo activo.</param>
    public sealed record MedidasSeccion(Func<string?, TipoBarra?> Barra, string? BarraEstribo);

    /// <summary>
    /// La sección real, para dibujarla a escala y con sus medidas (#133, #134):
    /// lados y recubrimiento en metros, diámetros en mm. Con
    /// <paramref name="DiametrosDeReserva"/> todavía falta elegir alguna barra y
    /// se dibuja con diámetros aproximados. <paramref name="DobladoEstriboMm"/>:
    /// diámetro de doblado del estribo principal (0 = 4 diámetros).
    /// </summary>
    public sealed record SeccionReal(
        double LadoX, double LadoY, double Recubrimiento,
        double DiametroEsquinaMm, double DiametroCaraXMm, double DiametroCaraYMm, double DiametroEstriboMm,
        bool DiametrosDeReserva, double DobladoEstriboMm = 0);

    private readonly MedidasSeccion? _medidas;
    private readonly SeccionReal _seccion;

    /// <summary>Píxeles por metro, la misma en los dos ejes.</summary>
    private readonly double _escala;

    /// <summary>Del borde de la sección al eje de cada barra (m), como <see cref="SeccionPlanta"/>.</summary>
    private readonly double _insetEsquina, _insetCaraX, _insetCaraY, _insetReferencia;

    /// <summary>Diámetro de doblado del estribo principal, en metros.</summary>
    private readonly double _doblezEstribo;

    /// <summary>Las cuatro esquinas (m), metidas en la curva del estribo como en el generador.</summary>
    private readonly IReadOnlyList<(double X, double Y)> _esquinas;

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

    /// <summary>
    /// Los choques de la sección con estas piezas (#131): la misma cuenta que
    /// la vista previa (<see cref="ChoquesSeccion.DeTramo"/>), con las piezas
    /// que se están editando. null = no se conocen las barras; no se marca nada.
    /// </summary>
    private readonly Func<IReadOnlyList<EstriboMenor>, ChoquesSeccion.Resultado>? _choquesDe;

    /// <summary>Los últimos choques calculados, para las notas.</summary>
    private ChoquesSeccion.Resultado _choques = ChoquesSeccion.Resultado.Vacio;

    public VentanaEstribosMenores(
        int barrasPorCaraX, int barrasPorCaraY, IEnumerable<EstriboMenor> piezas, IReadOnlyList<string> barrasAptas,
        SeccionReal seccion, MedidasSeccion? medidas = null,
        Func<IReadOnlyList<EstriboMenor>, ChoquesSeccion.Resultado>? choquesDe = null)
    {
        InitializeComponent();
        _choquesDe = choquesDe;
        _medidas = medidas;
        _seccion = seccion;
        _escala = EscalaDibujo.Ajustar(seccion.LadoX, seccion.LadoY, CajaDibujo, CajaDibujo);

        double InsetDe(double diametroMm) =>
            SeccionPlanta.Inset(seccion.Recubrimiento, seccion.DiametroEstriboMm / 1000, diametroMm / 1000);
        _insetEsquina = InsetDe(seccion.DiametroEsquinaMm);
        _insetCaraX = InsetDe(seccion.DiametroCaraXMm);
        _insetCaraY = InsetDe(seccion.DiametroCaraYMm);
        // Las intermedias se reparten entre las esquinas corridas con la barra
        // más gruesa, igual que en SeccionPlanta.Calcular.
        _insetReferencia = Math.Max(_insetEsquina, Math.Max(_insetCaraX, _insetCaraY));

        _doblezEstribo = (seccion.DobladoEstriboMm > 0 ? seccion.DobladoEstriboMm : 4 * seccion.DiametroEstriboMm) / 1000;
        var dobleces = SeccionPlanta.DoblecesEstribo(
            seccion.LadoX, seccion.LadoY, seccion.Recubrimiento, seccion.DiametroEstriboMm / 1000, _doblezEstribo).ToList();
        _esquinas = new[]
            {
                (_insetEsquina, _insetEsquina), (seccion.LadoX - _insetEsquina, _insetEsquina),
                (seccion.LadoX - _insetEsquina, seccion.LadoY - _insetEsquina), (_insetEsquina, seccion.LadoY - _insetEsquina),
            }
            .Select(p => SeccionPlanta.EnElDoblez(dobleces, p.Item1, p.Item2, seccion.DiametroEsquinaMm / 2000, SeccionPlanta.AlcanceDoblez))
            .ToList();

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

        TextoEncabezado.Text =
            $"Sección {EscalaDibujo.Cm(seccion.LadoX)} × {EscalaDibujo.Cm(seccion.LadoY)} · recubrimiento {EscalaDibujo.Cm(seccion.Recubrimiento)} · " +
            $"X = {barrasPorCaraX} · Y = {barrasPorCaraY} (+ 4 esquinas). Vista desde arriba, a escala, como en el modelo." +
            (seccion.DiametrosDeReserva ? " Faltan barras por elegir: se dibujan con un diámetro aproximado." : "");

        DibujarIcono(IconoGrapaC, FormaMenor.GrapaC);
        DibujarIcono(IconoGrapaS, FormaMenor.GrapaS);
        DibujarIcono(IconoCerrado, FormaMenor.Cerrado);

        Dibujar();
    }

    // ----------------------------------------------------------------- geometría

    /// <summary>De metros desde la cara izquierda de la sección a píxeles.</summary>
    private double PX(double x) => MargenIzquierdo + x * _escala;

    /// <summary>De metros desde la cara de abajo a píxeles (en el lienzo la Y crece hacia abajo).</summary>
    private double PY(double y) => MargenArriba + (_seccion.LadoY - y) * _escala;

    /// <summary>Dónde van las intermedias de las caras izquierda y derecha (las de eje <see cref="EjeCaja.Fila"/>).</summary>
    private double Izquierda => PX(_insetCaraY);
    private double Derecha => PX(_seccion.LadoX - _insetCaraY);

    /// <summary>Dónde van las intermedias de las caras de arriba y de abajo (las de eje <see cref="EjeCaja.Columna"/>).</summary>
    private double Arriba => PY(_seccion.LadoY - _insetCaraX);
    private double Abajo => PY(_insetCaraX);

    /// <summary>Fila 0 abajo, como en el modelo; repartidas parejo entre las esquinas, como en el generador.</summary>
    private double Y(int fila) =>
        PY(SeccionPlanta.PosicionIntermedia(_insetReferencia, _seccion.LadoY - _insetReferencia, _filas, fila));

    /// <summary>Columna 0 a la izquierda.</summary>
    private double X(int columna) =>
        PX(SeccionPlanta.PosicionIntermedia(_insetReferencia, _seccion.LadoX - _insetReferencia, _columnas, columna));

    /// <summary>Radio en píxeles de una barra, a escala (el mínimo es solo para que no desaparezca; se elige con <see cref="RadioCaptura"/>).</summary>
    private double Radio(double diametroMm) => Math.Max(1.5, diametroMm / 2000 * _escala);

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

    /// <summary>
    /// La pieza puesta bajo el punto, con la forma con la que se DIBUJA (la de
    /// Revit, <see cref="TrazoDe"/>): primero la que tiene la barra bajo el
    /// puntero -- con unos píxeles de margen; si hay varias, la que se ve encima
    /// y después la más cercana --,
    /// y si no, el cerrado más chico que lo encierra. Antes se buscaba con la
    /// forma esquemática vieja y había que hacer clic donde estaba la línea.
    /// </summary>
    private EstriboMenor? PiezaEn(Point p)
    {
        const double margen = 5;

        var trazos = _piezas.Select(o => (Pieza: o, Trazo: TrazoDe(o))).ToList();

        (EstriboMenor Pieza, double Distancia)? masCerca = trazos
            .Select(t => (t.Pieza, Distancia: DistanciaAlTrazo(p, t.Trazo.Puntos) - t.Trazo.Grosor / 2))
            .Where(t => t.Distancia <= margen)
            // Donde se cruzan, gana la que se ve encima: las grapas se dibujan sobre los cerrados.
            .OrderBy(t => t.Pieza.Forma == FormaMenor.Cerrado ? 1 : 0)
            .ThenBy(t => t.Distancia)
            .Cast<(EstriboMenor, double)?>()
            .FirstOrDefault();
        if (masCerca is { } cerca) return cerca.Pieza;

        return trazos
            .Where(t => t.Pieza.Forma == FormaMenor.Cerrado)
            .Select(t => (t.Pieza, Caja: Caja(t.Trazo.Puntos)))
            .Where(t => t.Caja.Contains(p))
            .OrderBy(t => t.Caja.Width * t.Caja.Height)
            .Select(t => t.Pieza)
            .FirstOrDefault();
    }

    /// <summary>Distancia en píxeles del punto al eje dibujado (una polilínea).</summary>
    private static double DistanciaAlTrazo(Point p, IReadOnlyList<Point> puntos)
    {
        double mejor = double.MaxValue;
        for (int i = 0; i + 1 < puntos.Count; i++)
        {
            Vector ab = puntos[i + 1] - puntos[i], ap = p - puntos[i];
            double t = ab.LengthSquared <= 1e-12 ? 0 : Math.Clamp((ap.X * ab.X + ap.Y * ab.Y) / ab.LengthSquared, 0, 1);
            mejor = Math.Min(mejor, (p - (puntos[i] + t * ab)).Length);
        }
        return mejor;
    }

    private static Rect Caja(IReadOnlyList<Point> puntos) => puntos.Count == 0
        ? Rect.Empty
        : new Rect(new Point(puntos.Min(q => q.X), puntos.Min(q => q.Y)), new Point(puntos.Max(q => q.X), puntos.Max(q => q.Y)));

    // ------------------------------------------------------------------- dibujo

    private void Dibujar()
    {
        double ancho = _seccion.LadoX * _escala, alto = _seccion.LadoY * _escala;
        Lienzo.Children.Clear();
        Lienzo.Width = MargenIzquierdo + ancho + MargenDerecho;
        Lienzo.Height = MargenArriba + alto + MargenAbajo;

        // Sección (el hormigón, a escala) y estribo principal como lo deja
        // Revit: cara exterior al recubrimiento, esquinas curvas y los ganchos
        // de 135° abajo a la izquierda (PlantaArmado).
        Agregar(new Rectangle { Width = ancho, Height = alto, Fill = Paleta.Hormigon, Stroke = Paleta.Contorno }, PX(0), PY(_seccion.LadoY));
        double rec = _seccion.Recubrimiento, dE = _seccion.DiametroEstriboMm;
        Lienzo.Children.Add(DibujoArmado.Trazo(
            PlantaArmado.EstriboCerrado(rec, _seccion.LadoX - rec, rec, _seccion.LadoY - rec, dE / 1000, _doblezEstribo, PlantaArmado.Cola135(dE)),
            AP, ColorEstribo, Math.Max(1.5, dE / 1000 * _escala)));

        // Los cerrados abajo de todo, para que no tapen a las grapas.
        foreach (EstriboMenor pieza in _piezas.OrderBy(p => p.Forma == FormaMenor.Cerrado ? 0 : 1))
            DibujarPieza(pieza, ColorDePieza(pieza), pieza == _seleccionada, opacidad: 1);

        // Vista previa semitransparente de lo que quedaría (en rojo si no se podría doblar).
        if (_mouse is Point mouse && PiezaPropuesta(mouse) is { } propuesta)
            DibujarPieza(propuesta, ColorDePieza(propuesta), seleccionada: false, opacidad: 0.45);

        // Barras: esquinas e intermedias, con la que está bajo el mouse resaltada.
        foreach ((double x, double y) in _esquinas)
            Punto(PX(x), PY(y), Radio(_seccion.DiametroEsquinaMm), Paleta.Superficie);

        (EjeCaja Eje, int Indice)? bajoMouse = _mouse is Point m && _herramienta is not null ? BarraEn(m) : null;
        for (int f = 0; f < _filas; f++)
            foreach (double x in new[] { Izquierda, Derecha })
                PuntoIntermedio(x, Y(f), Radio(_seccion.DiametroCaraYMm), (EjeCaja.Fila, f), bajoMouse);
        for (int c = 0; c < _columnas; c++)
            foreach (double y in new[] { Arriba, Abajo })
                PuntoIntermedio(X(c), y, Radio(_seccion.DiametroCaraXMm), (EjeCaja.Columna, c), bajoMouse);

        // Los choques (#131): en la zona de traslapo, además las piezas corridas.
        _choques = _choquesDe?.Invoke(_piezas) ?? ChoquesSeccion.Resultado.Vacio;
        OpcionZonaTraslapo.IsEnabled = _choques.HayTraslapo;
        if (!_choques.HayTraslapo && OpcionZonaTraslapo.IsChecked == true) OpcionSeccionNormal.IsChecked = true;
        bool verTraslapo = OpcionZonaTraslapo.IsChecked == true;
        if (verTraslapo)
            foreach (ChoquesSeccion.PiezaRedonda pieza in _choques.PiezasTraslapo)
                Punto(PX(pieza.X), PY(pieza.Y), Radio(pieza.DiametroMm), Paleta.Superficie);
        foreach (ChoquesSeccion.Choque choque in verTraslapo ? _choques.Traslapo : _choques.Normal)
            foreach ((double X, double Y, double Radio) marca in choque.Marcas)
                Lienzo.Children.Add(DibujoArmado.MarcaChoque(AP(marca.X, marca.Y), Math.Max(1.5, marca.Radio * _escala), choque.Motivo));

        // Rótulos de fila y columna, desde 1, por fuera de la sección.
        for (int f = 0; f < _filas; f++) Texto($"F{f + 1}", PX(0) - 30, Y(f) - 8);
        for (int c = 0; c < _columnas; c++) Texto($"C{c + 1}", X(c) - 8, PY(0) + 3);

        // Las medidas de la sección (#134).
        Cotas.Horizontal(Lienzo, PX(0), PX(_seccion.LadoX), PY(0) + 24, EscalaDibujo.Cm(_seccion.LadoX));
        Cotas.Vertical(Lienzo, PX(_seccion.LadoX) + 14, PY(_seccion.LadoY), PY(0), EscalaDibujo.Cm(_seccion.LadoY));

        ActualizarPaleta();
        ActualizarPanelPieza();
        ActualizarNotas();
    }

    private void PuntoIntermedio(double x, double y, double radio, (EjeCaja, int) barra, (EjeCaja, int)? bajoMouse)
    {
        bool primero = _primerClic is { } p && p.Equals(barra);
        double aro = Math.Max(11, radio + 5);
        if (primero || Equals(bajoMouse, barra))
            Agregar(new Ellipse { Width = 2 * aro, Height = 2 * aro, Stroke = ColorDe(_herramienta ?? FormaMenor.Cerrado), StrokeThickness = 2 }, x - aro, y - aro);
        Punto(x, y, radio, Paleta.ErrorClaro);
    }

    /// <summary>
    /// La pieza como la va a crear el generador (<see cref="PlantaArmado.Menor"/>):
    /// abrazando sus barras, con sus ganchos y el grosor de su barra a escala.
    /// La seleccionada lleva un halo alrededor.
    /// </summary>
    private void DibujarPieza(EstriboMenor pieza, Brush color, bool seleccionada, double opacidad)
    {
        (IReadOnlyList<(double X, double Y)> eje, double grosor) = EjeDe(pieza);

        if (seleccionada) Lienzo.Children.Add(DibujoArmado.Trazo(eje, AP, ColorSeleccion, grosor + 6, 0.35 * opacidad));
        Lienzo.Children.Add(DibujoArmado.Trazo(eje, AP, seleccionada ? ColorSeleccion : color, grosor, opacidad));
    }

    /// <summary>El eje de la pieza en metros (<see cref="PlantaArmado.Menor"/>) y el grosor con que se dibuja, en píxeles.</summary>
    private (IReadOnlyList<(double X, double Y)> Eje, double Grosor) EjeDe(EstriboMenor pieza)
    {
        TipoBarra? barra = _medidas?.Barra(pieza.TipoBarraNombre ?? _medidas.BarraEstribo);
        double diametroMm = barra?.DiametroMm ?? _seccion.DiametroEstriboMm;
        double doblez = barra is not null ? PlantaArmado.Doblez(barra, diametroMm) : _doblezEstribo;

        IReadOnlyList<(double X, double Y)> eje = PlantaArmado.Menor(
            pieza, k => BarrasDe(pieza.Eje, k), (_seccion.LadoX / 2, _seccion.LadoY / 2), diametroMm, doblez);
        return (eje, Math.Max(2, diametroMm / 1000 * _escala));
    }

    /// <summary>Lo mismo que se dibuja, en píxeles del lienzo: para saber qué pieza está bajo el puntero.</summary>
    private (IReadOnlyList<Point> Puntos, double Grosor) TrazoDe(EstriboMenor pieza)
    {
        (IReadOnlyList<(double X, double Y)> eje, double grosor) = EjeDe(pieza);
        return (eje.Select(q => AP(q.X, q.Y)).ToList(), grosor);
    }

    /// <summary>Las dos barras (m) que engancha una pieza en la fila o columna <paramref name="k"/>: la de la izquierda (o abajo) y la de enfrente.</summary>
    private (GanchoGrapa.Pieza Inicio, GanchoGrapa.Pieza Fin) BarrasDe(EjeCaja eje, int k)
    {
        if (eje == EjeCaja.Fila)
        {
            double y = SeccionPlanta.PosicionIntermedia(_insetReferencia, _seccion.LadoY - _insetReferencia, _filas, k);
            double r = _seccion.DiametroCaraYMm / 2000;
            return (new(_insetCaraY, y, r, 0), new(_seccion.LadoX - _insetCaraY, y, r, 0));
        }

        double x = SeccionPlanta.PosicionIntermedia(_insetReferencia, _seccion.LadoX - _insetReferencia, _columnas, k);
        double rx = _seccion.DiametroCaraXMm / 2000;
        return (new(x, _insetCaraX, rx, 0), new(x, _seccion.LadoY - _insetCaraX, rx, 0));
    }

    /// <summary>De metros en planta a píxeles del lienzo.</summary>
    private Point AP(double x, double y) => new(PX(x), PY(y));

    private Brush ColorDePieza(EstriboMenor pieza) =>
        PaqueteEstribos.EstaTapada(pieza, _piezas) ? ColorTapada
        : Imposible(pieza) is not null ? ColorImposible
        : ColorDe(pieza.Forma);

    /// <summary>
    /// Por qué un estribo cerrado no se puede doblar con su barra, o null si
    /// se puede (o si no se conocen las barras): ver
    /// <see cref="ChoquesSeccion.CerradoImposible"/>.
    /// </summary>
    private string? Imposible(EstriboMenor pieza)
    {
        if (pieza.Forma != FormaMenor.Cerrado || _medidas is null) return null;
        if (_medidas.Barra(pieza.TipoBarraNombre ?? _medidas.BarraEstribo) is not { } barra) return null;

        IEnumerable<GanchoGrapa.Pieza> abrazadas = Enumerable.Range(pieza.Desde, pieza.Hasta - pieza.Desde + 1)
            .SelectMany(k => { (GanchoGrapa.Pieza a, GanchoGrapa.Pieza b) = BarrasDe(pieza.Eje, k); return new[] { a, b }; });
        return ChoquesSeccion.CerradoImposible(pieza, barra, abrazadas, Donde(pieza));
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

    private void OpcionSeccion_Checked(object sender, RoutedEventArgs e)
    {
        // El XAML marca "Sección normal" mientras se construye la ventana, antes de tener sección.
        if (IsLoaded) Dibujar();
    }

    private void ActualizarNotas()
    {
        // Los choques del modo elegido (#131); el cerrado imposible ya viene de Imposible.
        IEnumerable<ChoquesSeccion.Choque> choques = OpcionZonaTraslapo.IsChecked == true ? _choques.Traslapo : _choques.Normal;
        List<string> imposibles = _piezas.Select(Imposible).OfType<string>().ToList();
        NotaTapadas.Text = string.Join("\n", _piezas
            .Where(p => PaqueteEstribos.EstaTapada(p, _piezas))
            .Select(p => $"La {Nombre(p.Forma).ToLowerInvariant()} de la {Donde(p)} queda dentro de un estribo cerrado y no se va a generar.")
            .Concat(imposibles)
            .Concat(choques.Where(c => !imposibles.Contains(c.Motivo)).Select(c => "⚠ " + c.Motivo)));

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
