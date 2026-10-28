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
/// Pestaña «Vista previa»: la columna activa vista desde arriba, y el tramo
/// activo de frente (cara paralela a X) y de costado (cara paralela a Y), con
/// cada barra del color de su diámetro (<see cref="ColoresVarilla"/>). Todo
/// sale de las mismas cuentas que usan los generadores
/// (<see cref="SeccionPlanta"/>, <see cref="RepartoEstribos"/>,
/// <see cref="PaqueteEstribos"/>): lo que se ve es lo que se genera.
/// docs/planes/2026-10-20-diseno-vista-previa-y-colores.md, sección 2.
/// </summary>
public partial class PanelVistaPrevia : UserControl
{
    private static readonly Brush ColorHormigon = Paleta.Hormigon;
    private static readonly Brush ColorContorno = Paleta.Contorno;
    private static readonly Brush ColorTexto = Paleta.TextoSecundario;
    private static readonly Brush ColorNudo = new SolidColorBrush(Paleta.ConAlfa(Paleta.ColorNudo, 0x60));
    private static readonly Brush ColorZapata = new SolidColorBrush(Paleta.ConAlfa(Paleta.ColorZapata, 0x60));
    private static readonly Brush ColorAviso = Paleta.AvisoTexto;
    private static readonly Brush ColorTapada = Paleta.TapadaClara;

    private const double AnchoLienzo = 350;
    private const double AltoPlanta = 190;
    private const double YElevaciones = AltoPlanta + 24;
    private const double AltoElevaciones = 300;
    /// <summary>En cada elevación: a la izquierda las cotas por zona, a la derecha la del alto del tramo.</summary>
    private const double MargenCotasZonas = 34;
    private const double MargenCotaAlto = 22;

    /// <summary>
    /// Una barra en la elevación: dónde cae a lo ancho de la vista (metros) y
    /// su diámetro. <paramref name="Gancho"/>: hacia dónde dobla su gancho a lo
    /// ancho de la vista (−1 o 1), o null si no lleva o dobla hacia adentro
    /// de la hoja (#181).
    /// </summary>
    private sealed record BarraVista(double Posicion, double DiametroMm, GanchosLongitudinales.Gancho? DatosGancho = null, int? Gancho = null);

    /// <summary>Con «Columna completa» las vistas de lado son el doble de altas: entra toda la columna.</summary>
    private const double AltoElevacionesColumna = 2 * AltoElevaciones;

    public PanelVistaPrevia() => InitializeComponent();

    /// <summary>Vuelve a dibujar lo último que se mostró (al cambiar "Sección normal / Zona de traslapo").</summary>
    private Action? _redibujar;

    /// <summary>Mientras Mostrar acomoda el selector: sus cambios no tienen que volver a dibujar.</summary>
    private bool _ajustandoSelector;

    private void OpcionSeccion_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ajustandoSelector) _redibujar?.Invoke();
    }

    /// <summary>Sin dibujo: solo un texto (por ejemplo, "Elige una columna").</summary>
    public void MostrarMensaje(string mensaje)
    {
        _redibujar = null;
        // Sin dibujo no hay nada que elegir.
        OpcionSeccionNormal.IsEnabled = false;
        OpcionZonaTraslapo.IsEnabled = false;
        Lienzo.Children.Clear();
        Leyenda.Children.Clear();
        Resumen.Children.Clear();
        AgregarNota(mensaje, ColorTexto);
    }

    /// <param name="cfgLong">Con los estribos menores de la columna ya cargados.</param>
    /// <param name="barraDe">Un tipo de barra por su nombre (diámetro y doblado), o null si no existe.</param>
    /// <param name="diametroEstriboPlantaMm">
    /// El estribo más grueso de la columna: el generador corre las longitudinales
    /// con ese (<c>GeneradorLongitudinal</c>, "diametroEstribo").
    /// </param>
    /// <param name="origenEnPlanta">
    /// Dónde cae en planta la esquina de abajo a la izquierda de la sección, en
    /// metros: solo para girar los ganchos que cruzarían la línea de propiedad.
    /// </param>
    public void Mostrar(
        ColumnaContexto columna, Tramo tramo, ConfigTramo cfgTramo, ConfigLongitudinal cfgLong,
        double recubrimiento, Func<string?, TipoBarra?> barraDe, double diametroEstriboPlantaMm,
        (double X, double Y) origenEnPlanta = default)
    {
        _redibujar = () => Mostrar(columna, tramo, cfgTramo, cfgLong, recubrimiento, barraDe, diametroEstriboPlantaMm, origenEnPlanta);
        Lienzo.Children.Clear();
        Leyenda.Children.Clear();
        Resumen.Children.Clear();

        double? diametroDeBarraMm(string? nombre) => string.IsNullOrEmpty(nombre) ? null : barraDe(nombre)?.DiametroMm;

        double? dEsquina = diametroDeBarraMm(cfgLong.BarraDe(PosicionBarra.Esquina));
        double? dCaraX = diametroDeBarraMm(cfgLong.BarraDe(PosicionBarra.CaraX));
        double? dCaraY = diametroDeBarraMm(cfgLong.BarraDe(PosicionBarra.CaraY));
        if (dEsquina is null || dCaraX is null || dCaraY is null)
        {
            MostrarMensaje("Elige la barra de las esquinas, de la cara X y de la cara Y para ver la vista previa.");
            return;
        }

        double DiametroDe(PosicionBarra p) => p switch
        {
            PosicionBarra.Esquina => dEsquina.Value,
            PosicionBarra.CaraX => dCaraX.Value,
            _ => dCaraY.Value,
        };

        // El estribo principal del tramo activo y su paquete con los menores
        // (en los nudos no van menores, como en GeneradorArmado). El tramo
        // contra zapata no lleva estribos (Luciano, 20-oct): no se dibujan.
        bool sinEstribos = tramo.Tipo == TipoTramo.ContraZapata;
        double? dEstribo = sinEstribos ? null : diametroDeBarraMm(cfgTramo.TipoBarraNombre);

        // Las esquinas, metidas en la curva del estribo como las deja el
        // generador (sin estribo, donde da la cuenta). Las barras y los menores
        // se calculan en la zona común de la columna, donde las pone el
        // generador; el hormigón y el estribo, con la medida del tramo, y lo
        // primero se corre (dx, dy) a su lugar dentro del tramo. Con la misma
        // sección en toda la columna, el corrimiento es 0 (dado de 40×40 bajo
        // una de 30×30: 22-oct).
        double ladoX = columna.BaseX, ladoY = columna.BaseY;
        (double tramoX, double tramoY, double dx, double dy) = tramo.Seccion(columna);
        double doblezEstribo = dEstribo is double dd ? PlantaArmado.Doblez(barraDe(cfgTramo.TipoBarraNombre), dd) : 0;
        IReadOnlyList<SeccionPlanta.BarraEnPlanta> barras = SeccionPlanta.Calcular(
            ladoX, ladoY, recubrimiento, diametroEstriboPlantaMm, cfgLong, DiametroDe, doblezEstribo * 1000);
        List<EstriboMenor> piezas = tramo.Tipo is TipoTramo.DentroDeViga or TipoTramo.ContraZapata
            ? new List<EstriboMenor>()
            : cfgLong.MenoresParaGenerar();
        double DiametroPieza(EstriboMenor p) => diametroDeBarraMm(p.TipoBarraNombre) ?? dEstribo ?? 0;

        PaqueteEstribos.Paquete? paquete = null;
        RepartoEstribos.Resultado? reparto = null;
        if (dEstribo is double d)
        {
            paquete = PaqueteEstribos.Calcular(piezas, d, DiametroPieza);
            reparto = RepartoEstribos.Calcular(tramo.ZInferior, tramo.ZSuperior, cfgTramo, paquete);
        }

        // Dónde cae cada fila / columna de intermedias (para ubicar los menores).
        int columnas = Math.Max(0, cfgLong.BarrasPorCaraX / 2);
        int filas = Math.Max(0, cfgLong.BarrasPorCaraY / 2);
        double insetEsq = SeccionPlanta.Inset(recubrimiento, diametroEstriboPlantaMm / 1000, barras.Max(b => b.DiametroMm) / 1000);
        double XColumna(int c) => SeccionPlanta.PosicionIntermedia(insetEsq, ladoX - insetEsq, columnas, c);
        double YFila(int f) => SeccionPlanta.PosicionIntermedia(insetEsq, ladoY - insetEsq, filas, f);

        double DoblezPieza(EstriboMenor p) =>
            PlantaArmado.Doblez(barraDe(p.TipoBarraNombre) ?? barraDe(cfgTramo.TipoBarraNombre), DiametroPieza(p));

        // Los choques (#131), de la sección normal o de la zona de traslapo
        // según el selector; sin traslapo, el selector queda en "normal".
        ChoquesSeccion.Resultado choques = ChoquesSeccion.DeTramo(
            columna, tramo, cfgTramo, cfgLong, recubrimiento, barraDe, diametroEstriboPlantaMm);
        _ajustandoSelector = true;
        OpcionSeccionNormal.IsEnabled = true;
        OpcionZonaTraslapo.IsEnabled = choques.HayTraslapo;
        if (!choques.HayTraslapo) OpcionSeccionNormal.IsChecked = true;
        _ajustandoSelector = false;
        bool verTraslapo = OpcionZonaTraslapo.IsChecked == true;
        IReadOnlyList<ChoquesSeccion.Choque> aMarcar = verTraslapo ? choques.Traslapo : choques.Normal;

        DibujarPlanta(tramoX, tramoY, (ladoX / 2, ladoY / 2), dx, dy, recubrimiento, dEstribo, doblezEstribo, barras, piezas,
            DiametroPieza, DoblezPieza, XColumna, YFila, columnas, filas, aMarcar, verTraslapo ? choques.PiezasTraslapo : null);

        // #181: los ganchos de 90° de las puntas, con la misma regla que el
        // generador (hacia el centro; girado si cruza el lindero). Solo se
        // dibujan de lado: desde arriba taparían las varillas (Luciano, 28-oct).
        bool conGanchos = PlanTraslapos.Columna.De(columna).ConGanchos;
        Dictionary<SeccionPlanta.BarraEnPlanta, GanchosLongitudinales.Gancho> ganchos = conGanchos
            ? GanchosLongitudinales.Calcular(barras, ladoX, ladoY, columna.Lindero, origenEnPlanta).ToDictionary(g => g.Barra)
            : new Dictionary<SeccionPlanta.BarraEnPlanta, GanchosLongitudinales.Gancho>();
        BarraVista Vista(SeccionPlanta.BarraEnPlanta b, double posicion, bool aLoAnchoEnX)
        {
            if (!ganchos.TryGetValue(b, out GanchosLongitudinales.Gancho? g)) return new BarraVista(posicion, b.DiametroMm);
            int aLoAncho = aLoAnchoEnX ? g.Dx : g.Dy;
            return new BarraVista(posicion, b.DiametroMm, g, aLoAncho == 0 ? null : aLoAncho);
        }

        // Frente: se mira la cara de abajo (paralela a X); costado: la de la izquierda (paralela a Y).
        List<BarraVista> frente = barras
            .Where(b => b.Y < ladoY / 2 && b.Posicion != PosicionBarra.CaraY)
            .OrderBy(b => b.Posicion)
            .Select(b => Vista(b, b.X + dx, aLoAnchoEnX: true)).ToList();
        List<BarraVista> costado = barras
            .Where(b => b.X < ladoX / 2 && b.Posicion != PosicionBarra.CaraX)
            .OrderBy(b => b.Posicion)
            .Select(b => Vista(b, b.Y + dy, aLoAnchoEnX: false)).ToList();

        Tramo? debajo = columna.Tramos.FirstOrDefault(t => t.Indice == tramo.Indice - 1);
        Tramo? encima = columna.Tramos.FirstOrDefault(t => t.Indice == tramo.Indice + 1);

        // Los vecinos (nudo o zapata) se muestran recortados, solo para ubicar el
        // tramo. Las dos elevaciones van a la misma escala, y el ancho a la misma
        // que el alto (#133: nada exagerado). Si la columna es tan ancha que no
        // entra, se achica todo junto. Con «Columna completa», de punta a punta.
        // Abajo de todo se agrega lo que el acero entra en la zapata, para que
        // se vea el gancho.
        bool columnaCompleta = OpcionElevacionColumna.IsChecked == true;
        double zBase = columna.Tramos.Count > 0 ? columna.Tramos[0].ZInferior : columna.ZInferior;
        double anclaje = Math.Max(0, zBase - columna.ZInferiorLongitudinal);
        double anchoVista = (AnchoLienzo - 3 * 16) / 2;
        double zMin = columnaCompleta ? zBase : tramo.ZInferior - (debajo is null ? 0 : Math.Min(debajo.Altura, 0.4));
        double zMax = columnaCompleta ? columna.ZSuperior : tramo.ZSuperior + (encima is null ? 0 : Math.Min(encima.Altura, 0.4));
        if (anclaje > 0 && (columnaCompleta || debajo is null)) zMin -= anclaje + 0.05;
        // Un poco de aire arriba para que el gancho no toque el título.
        if (conGanchos && (columnaCompleta || encima is null)) zMax += columnaCompleta ? (zMax - zMin) * 0.02 : 0.05;
        double altoElevaciones = columnaCompleta ? AltoElevacionesColumna : AltoElevaciones;
        double anchoUtil = anchoVista - MargenCotasZonas - MargenCotaAlto;
        double escalaElevacion = EscalaDibujo.Ajustar(Math.Max(tramoX, tramoY), zMax - zMin, anchoUtil, altoElevaciones);

        // Con la columna entera, a una sola escala la sección queda de unos
        // pocos píxeles y los ganchos no se ven: el ancho ocupa todo el ancho
        // de la vista y el alto, todo el alto. Se avisa en el resumen.
        double escalaAncho = columnaCompleta ? anchoUtil / Math.Max(tramoX, tramoY) : escalaElevacion;
        var vista = new VistaDeLado(columna, tramo, debajo, encima, columnaCompleta, zMin, zMax, escalaAncho, escalaElevacion);
        DibujarElevacion("De frente (cara X)", 16, tramoX, vista, frente, dEstribo,
            reparto, paquete, DiametroPieza, EjeCaja.Fila, c => XColumna(c) + dx);
        DibujarElevacion("De costado (cara Y)", 32 + anchoVista, tramoY, vista, costado, dEstribo,
            reparto, paquete, DiametroPieza, EjeCaja.Columna, f => YFila(f) + dy);

        Lienzo.Height = YElevaciones + altoElevaciones + 30;

        LlenarLeyenda(barras.Select(b => b.DiametroMm)
            .Concat(dEstribo is double de ? new[] { de } : Array.Empty<double>())
            .Concat(piezas.Select(DiametroPieza)));
        LlenarResumen(cfgLong, barras, dEstribo, reparto, piezas, DiametroPieza, sinEstribos);

        int cuantos = choques.Normal.Count + choques.Traslapo.Count;
        if (cuantos > 0)
            AgregarNota(
                $"⚠ {cuantos} choque(s) (se puede generar igual): {choques.Normal.Count} en la sección normal, " +
                $"{choques.Traslapo.Count} en la zona de traslapo. Pasa el mouse por las marcas rojas para ver el motivo.",
                Paleta.Error, negrita: true);
        if (choques.VarillasSinRevisarEnTraslapo > 0)
            AgregarNota(ChoquesSeccion.NotaSinRevisar(choques.VarillasSinRevisarEnTraslapo), ColorAviso);

        LlenarNotasDeGanchos(ganchos.Values.ToList(), ladoX, ladoY, columnaCompleta);
    }

    /// <summary>
    /// #181: si algún gancho se sale de la columna, en rojo y antes de
    /// generar. Abajo el gancho queda en la zapata (más ancha); arriba, en la
    /// viga o la losa, que el add-in no mide: se avisa igual.
    /// </summary>
    private void LlenarNotasDeGanchos(IReadOnlyList<GanchosLongitudinales.Gancho> ganchos, double ladoX, double ladoY, bool columnaCompleta)
    {
        if (columnaCompleta)
            AgregarNota("Columna completa: se ven el hormigón, las barras y sus ganchos (el alto va a otra escala que el ancho, " +
                        "para que entre); los estribos, en «Tramo activo».", ColorTexto);
        if (ganchos.Count == 0) return;

        AgregarNota("Ganchos de 90° en las dos puntas, hacia el centro. De lado se ven los que doblan a lo ancho de cada vista; " +
                    "los demás doblan hacia adentro de la hoja.", ColorTexto);

        List<GanchosLongitudinales.Gancho> fuera = ganchos.Where(g => g.SeSale).ToList();
        if (fuera.Count > 0)
            AgregarNota(
                $"⚠ {fuera.Count} gancho(s) se salen de la sección de {EscalaDibujo.Cm(ladoX)} × {EscalaDibujo.Cm(ladoY)} " +
                $"(la pata llega a {fuera.Max(g => g.Recto) * 100:0.#} cm del eje de la barra; en rojo). Abajo quedan dentro de la zapata; " +
                "arriba, revisa que entren en la viga o la losa.", Paleta.Error, negrita: true);
        if (ganchos.Any(g => g.Girado))
            AgregarNota($"{ganchos.Count(g => g.Girado)} gancho(s) girados para no cruzar la línea de propiedad.", ColorAviso);
        if (ganchos.Any(g => g.SinSalida))
            AgregarNota($"⚠ {ganchos.Count(g => g.SinSalida)} gancho(s) cruzan la línea de propiedad para los dos lados.", Paleta.Error, negrita: true);
    }

    // ------------------------------------------------------------- planta

    /// <param name="ladoX">Los lados del tramo: el hormigón, el estribo y las cotas.</param>
    /// <param name="centroComun">El centro de la zona común, donde se calcularon barras y menores.</param>
    /// <param name="dx">Lo que hay que correr lo calculado en la zona común para ponerlo en el tramo.</param>
    private void DibujarPlanta(
        double ladoX, double ladoY, (double X, double Y) centroComun, double dx, double dy,
        double recubrimiento, double? dEstribo, double doblezEstribo,
        IReadOnlyList<SeccionPlanta.BarraEnPlanta> barras, List<EstriboMenor> piezas, Func<EstriboMenor, double> diametroPieza,
        Func<EstriboMenor, double> doblezPieza, Func<int, double> xColumna, Func<int, double> yFila, int columnas, int filas,
        IReadOnlyList<ChoquesSeccion.Choque> choques, IReadOnlyList<ChoquesSeccion.PiezaRedonda>? piezasTraslapo)
    {
        Texto("Desde arriba", 16, 0, 11, FontWeights.SemiBold);

        double escala = EscalaDibujo.Ajustar(ladoX, ladoY, AnchoLienzo - 120, AltoPlanta - 60);
        double ancho = ladoX * escala, alto = ladoY * escala;
        double ox = (AnchoLienzo - ancho) / 2, oy = 22;
        Point P(double x, double y) => new(ox + x * escala, oy + alto - y * escala);
        Point Q(double x, double y) => P(x + dx, y + dy);

        Agregar(new Rectangle { Width = ancho, Height = alto, Fill = ColorHormigon, Stroke = ColorContorno }, ox, oy);

        // Estribo principal como lo deja Revit: cara exterior al recubrimiento,
        // esquinas curvas y los ganchos de 135° abajo a la izquierda.
        if (dEstribo is double de)
            Lienzo.Children.Add(DibujoArmado.Trazo(
                PlantaArmado.EstriboCerrado(recubrimiento, ladoX - recubrimiento, recubrimiento, ladoY - recubrimiento,
                    de / 1000, doblezEstribo, PlantaArmado.Cola135(de)),
                P, Pincel(de), Math.Max(1.2, de / 1000 * escala)));

        // Estribos menores, abrazando las barras que enganchan (PlantaArmado.Menor).
        List<SeccionPlanta.BarraEnPlanta> deCaraY = barras.Where(b => b.Posicion == PosicionBarra.CaraY).ToList();
        List<SeccionPlanta.BarraEnPlanta> deCaraX = barras.Where(b => b.Posicion == PosicionBarra.CaraX).ToList();
        (GanchoGrapa.Pieza, GanchoGrapa.Pieza) BarrasDe(EjeCaja eje, int k)
        {
            if (eje == EjeCaja.Fila)
            {
                double r = deCaraY[0].DiametroMm / 2000;
                return (new(deCaraY.Min(b => b.X), yFila(k), r, 0), new(deCaraY.Max(b => b.X), yFila(k), r, 0));
            }
            double rx = deCaraX[0].DiametroMm / 2000;
            return (new(xColumna(k), deCaraX.Min(b => b.Y), rx, 0), new(xColumna(k), deCaraX.Max(b => b.Y), rx, 0));
        }

        // Los cerrados abajo de todo, para que no tapen a las grapas.
        foreach (EstriboMenor p in piezas.OrderBy(p => p.Forma == FormaMenor.Cerrado ? 0 : 1))
        {
            if (p.Eje == EjeCaja.Fila ? p.Hasta >= filas || deCaraY.Count == 0 : p.Hasta >= columnas || deCaraX.Count == 0) continue;
            bool tapada = PaqueteEstribos.EstaTapada(p, piezas);
            Brush color = tapada ? ColorTapada : Pincel(diametroPieza(p));
            double grosor = Math.Max(1.2, diametroPieza(p) / 1000 * escala);
            // Un borde fino, como el de las barras: con el mismo diámetro que
            // el estribo grande salían del mismo color y no se veía dónde
            // empezaba cada uno (#182).
            Lienzo.Children.Add(DibujoArmado.Trazo(
                PlantaArmado.Menor(p, k => BarrasDe(p.Eje, k), centroComun, diametroPieza(p), doblezPieza(p)),
                Q, Paleta.Texto, grosor + 1, tapada ? 0.6 : 1));
            Lienzo.Children.Add(DibujoArmado.Trazo(
                PlantaArmado.Menor(p, k => BarrasDe(p.Eje, k), centroComun, diametroPieza(p), doblezPieza(p)),
                Q, color, grosor, tapada ? 0.6 : 1));
        }

        foreach (SeccionPlanta.BarraEnPlanta barra in barras)
        {
            // Su diámetro a escala; el mínimo es solo para que no desaparezca.
            double radio = Math.Max(1, barra.DiametroMm / 2000 * escala);
            Point c = Q(barra.X, barra.Y);
            Agregar(new Ellipse { Width = 2 * radio, Height = 2 * radio, Fill = Pincel(barra.DiametroMm), Stroke = Paleta.Texto, StrokeThickness = 0.5 },
                c.X - radio, c.Y - radio);
        }

        // Zona de traslapo: las piezas de las varillas que traslapan, con la
        // corrida al lado de la pedida (la pedida coincide con la barra).
        if (piezasTraslapo is not null)
            foreach (ChoquesSeccion.PiezaRedonda pieza in piezasTraslapo)
            {
                double radio = Math.Max(1, pieza.DiametroMm / 2000 * escala);
                Point c = Q(pieza.X, pieza.Y);
                Agregar(new Ellipse { Width = 2 * radio, Height = 2 * radio, Fill = Pincel(pieza.DiametroMm), Stroke = Paleta.Texto, StrokeThickness = 0.5 },
                    c.X - radio, c.Y - radio);
            }

        // Los choques, encima de todo para que se lean al pasar el mouse.
        foreach (ChoquesSeccion.Choque choque in choques)
            foreach ((double X, double Y, double Radio) marca in choque.Marcas)
                Lienzo.Children.Add(DibujoArmado.MarcaChoque(Q(marca.X, marca.Y), Math.Max(1, marca.Radio * escala), choque.Motivo));

        // Cuántas intermedias lleva cada cara.
        string cx = (columnas).ToString(), cy = (filas).ToString();
        Texto(cx, ox + ancho / 2 - 3, oy + alto + 2, 11);
        Texto(cx, ox + ancho / 2 - 3, oy - 15, 11);
        Texto(cy, ox - 12, oy + alto / 2 - 8, 11);
        Texto(cy, ox + ancho + 5, oy + alto / 2 - 8, 11);

        // Las medidas de la sección (#133).
        Cotas.Horizontal(Lienzo, ox, ox + ancho, oy + alto + 22, EscalaDibujo.Cm(ladoX));
        Cotas.Vertical(Lienzo, ox + ancho + 20, oy, oy + alto, EscalaDibujo.Cm(ladoY));
    }

    // ---------------------------------------------------------- elevación

    /// <summary>
    /// Lo que comparten las dos vistas de lado: qué se muestra (el tramo con
    /// sus vecinos recortados, o la columna entera) y a qué escala.
    /// </summary>
    /// <param name="EscalaAncho">Píxeles por metro a lo ancho; con el tramo, la misma que a lo alto (#133).</param>
    /// <param name="EscalaAlto">Píxeles por metro a lo alto.</param>
    private sealed record VistaDeLado(
        ColumnaContexto Columna, Tramo Tramo, Tramo? Debajo, Tramo? Encima, bool ColumnaCompleta, double ZMin, double ZMax,
        double EscalaAncho, double EscalaAlto);

    private void DibujarElevacion(
        string titulo, double x0, double lado, VistaDeLado vista,
        List<BarraVista> barras, double? dEstribo, RepartoEstribos.Resultado? reparto, PaqueteEstribos.Paquete? paquete,
        Func<EstriboMenor, double> diametroPieza, EjeCaja ejeQueCorre, Func<int, double> posicionIndice)
    {
        Texto(titulo, x0, YElevaciones - 18, 11, FontWeights.SemiBold);

        (ColumnaContexto columna, Tramo tramo, Tramo? debajo, Tramo? encima, bool columnaCompleta, double zMin, double zMax,
            double escala, double escalaAlto) = vista;
        double anchoDibujo = lado * escala;
        double ox = x0 + MargenCotasZonas;

        double Y(double z) => YElevaciones + (zMax - z) * escalaAlto;
        double X(double posicion) => ox + posicion * escala;

        void Bloque(Tramo? t, double z1, double z2)
        {
            if (t is null || z2 <= z1) return;
            Brush relleno = t.Tipo == TipoTramo.DentroDeViga ? ColorNudo : t.Tipo == TipoTramo.ContraZapata ? ColorZapata : ColorHormigon;
            Agregar(new Rectangle { Width = anchoDibujo, Height = Math.Max(0, (z2 - z1) * escalaAlto), Fill = relleno, Stroke = ColorContorno }, ox, Y(z2));
        }

        if (columnaCompleta)
        {
            foreach (Tramo t in columna.Tramos) Bloque(t, t.ZInferior, t.ZSuperior);
        }
        else
        {
            Agregar(new Rectangle { Width = anchoDibujo, Height = (tramo.ZSuperior - tramo.ZInferior) * escalaAlto, Fill = ColorHormigon, Stroke = ColorContorno },
                ox, Y(tramo.ZSuperior));
            Bloque(debajo, Math.Max(zMin, debajo?.ZInferior ?? zMin), tramo.ZInferior);
            Bloque(encima, tramo.ZSuperior, zMax);
        }

        // Donde el acero entra en la zapata (más abajo que la columna), una
        // franja más ancha, como la viga en el nudo de la elevación (#181).
        double zBase = columna.Tramos.Count > 0 ? columna.Tramos[0].ZInferior : columna.ZInferior;
        if (zMin < zBase - 1e-6)
        {
            double sobresale = Math.Min(0.15 * escala, 14);
            Agregar(new Rectangle { Width = anchoDibujo + 2 * sobresale, Height = (zBase - zMin) * escalaAlto, Fill = ColorZapata, Stroke = ColorContorno },
                ox - sobresale, Y(zBase));
        }

        // Barras longitudinales, de punta a punta de lo que se muestra; donde
        // la barra termina en un gancho que dobla a lo ancho de la vista, el
        // gancho (en rojo si se sale de la columna).
        double zDesde = Math.Max(zMin, columna.ZInferiorLongitudinal), zHasta = Math.Min(zMax, columna.ZSuperior);
        foreach (BarraVista b in barras)
        {
            double grosor = Math.Max(1.5, b.DiametroMm / 1000 * escala);
            GanchosLongitudinales.Gancho? g = b.Gancho is null ? null : b.DatosGancho;
            bool ganchoAbajo = g is not null && zDesde <= columna.ZInferiorLongitudinal + 1e-6;
            bool ganchoArriba = g is not null && zHasta >= columna.ZSuperior - 1e-6;
            double radio = g?.Radio ?? 0;
            Linea(new Point(X(b.Posicion), Y(ganchoArriba ? zHasta - radio : zHasta)), new Point(X(b.Posicion), Y(ganchoAbajo ? zDesde + radio : zDesde)),
                Pincel(b.DiametroMm), grosor, false);

            if (g is null || b.Gancho is not int signo) continue;
            Brush color = g.SeSale ? Paleta.Error : Pincel(b.DiametroMm);
            Point APx(double u, double z) => new(X(u), Y(z));
            if (ganchoAbajo) Lienzo.Children.Add(DibujoArmado.Trazo(EjeGancho(b.Posicion, zDesde, abajo: true, signo, g), APx, color, grosor));
            if (ganchoArriba) Lienzo.Children.Add(DibujoArmado.Trazo(EjeGancho(b.Posicion, zHasta, abajo: false, signo, g), APx, color, grosor));
        }

        // El ancho de la cara y el alto del tramo (o de la columna).
        double altoDesde = columnaCompleta ? zBase : tramo.ZInferior, altoHasta = columnaCompleta ? columna.ZSuperior : tramo.ZSuperior;
        Cotas.Horizontal(Lienzo, ox, ox + anchoDibujo, Y(zMin) + 5, EscalaDibujo.Cm(lado));
        Cotas.Vertical(Lienzo, ox + anchoDibujo + 6, Y(altoHasta), Y(altoDesde), EscalaDibujo.M(altoHasta - altoDesde));

        // Con la columna entera no se dibujan estribos: cada tramo tiene los suyos.
        if (columnaCompleta || reparto is null || paquete is null || dEstribo is not double de) return;

        double xIzq = barras.Min(b => b.Posicion), xDer = barras.Max(b => b.Posicion);
        foreach (double z in reparto.Alturas)
        {
            Linea(new Point(X(xIzq) - 3, Y(z)), new Point(X(xDer) + 3, Y(z)), Pincel(de), 1.4, false);

            // Los menores, a la altura que les toca en el paquete: línea si
            // corren a lo ancho de esta vista, punto si la atraviesan.
            double medio = z - paquete.DesplazamientoPrincipal;
            foreach (PaqueteEstribos.NivelPieza nivel in paquete.Piezas)
            {
                EstriboMenor p = nivel.Pieza;
                double zp = medio + nivel.Desplazamiento;
                Brush color = Pincel(diametroPieza(p));
                if (p.Eje == ejeQueCorre)
                    Linea(new Point(X(xIzq), Y(zp)), new Point(X(xDer), Y(zp)), color, 1, true);
                else if (p.Forma == FormaMenor.Cerrado)
                    Linea(new Point(X(posicionIndice(p.Desde)), Y(zp)), new Point(X(posicionIndice(p.Hasta)), Y(zp)), color, 1, true);
                else
                    Agregar(new Ellipse { Width = 3, Height = 3, Fill = color }, X(posicionIndice(p.Desde)) - 1.5, Y(zp) - 1.5);
            }
        }

        // Cotas por zona, a la izquierda.
        foreach (RepartoEstribos.Zona zona in reparto.Zonas)
        {
            double ya = Y(zona.ZDesde), yb = Y(zona.ZHasta), xc = x0 + 8;
            Linea(new Point(xc, ya), new Point(xc, yb), ColorTexto, 0.8, false);
            Linea(new Point(xc - 3, ya), new Point(xc + 3, ya), ColorTexto, 0.8, false);
            Linea(new Point(xc - 3, yb), new Point(xc + 3, yb), ColorTexto, 0.8, false);
            var t = new TextBlock { Text = $"@{zona.Espaciado * 100:0.#}", FontSize = 9, Foreground = ColorTexto };
            t.LayoutTransform = new RotateTransform(-90);
            Agregar(t, xc + 2, (ya + yb) / 2 - 12);
        }
    }

    /// <summary>
    /// El eje de un gancho de 90° en la vista de lado (#181): el arco del
    /// doblez y la pata horizontal, en la punta <paramref name="z"/> de la
    /// barra. <paramref name="u"/> es la posición de la barra a lo ancho de la
    /// vista y <paramref name="signo"/>, hacia dónde dobla.
    /// </summary>
    private static IEnumerable<(double X, double Y)> EjeGancho(double u, double z, bool abajo, int signo, GanchosLongitudinales.Gancho g)
    {
        double r = g.Radio;
        double cu = u + signo * r, cz = abajo ? z + r : z - r;
        const int Pasos = 8;
        for (int i = 0; i <= Pasos; i++)
        {
            // De la barra (al costado del centro del doblez) a la pata (abajo o arriba de él).
            double t = i / (double)Pasos * Math.PI / 2;
            yield return (cu - signo * r * Math.Cos(t), cz + (abajo ? -1 : 1) * r * Math.Sin(t));
        }
        yield return (u + signo * g.Recto, z);
    }

    // ------------------------------------------------------------ textos

    private void LlenarLeyenda(IEnumerable<double> diametros)
    {
        foreach (double d in diametros.Where(d => d > 0).GroupBy(d => ColoresVarilla.NumeroDe(d) ?? -1).Select(g => g.First()).OrderBy(d => d))
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 2) };
            item.Children.Add(new Rectangle { Width = 14, Height = 6, Fill = Pincel(d), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(new TextBlock { Text = Numero(d), FontSize = 10, Foreground = ColorTexto });
            Leyenda.Children.Add(item);
        }
    }

    private void LlenarResumen(
        ConfigLongitudinal cfg, IReadOnlyList<SeccionPlanta.BarraEnPlanta> barras, double? dEstribo,
        RepartoEstribos.Resultado? reparto, List<EstriboMenor> piezas, Func<EstriboMenor, double> diametroPieza,
        bool sinEstribos)
    {
        string NumeroDe(PosicionBarra p) => Numero(barras.First(b => b.Posicion == p).DiametroMm);
        int esquinas = barras.Count(b => b.Posicion == PosicionBarra.Esquina);
        var partes = new List<string> { $"esquinas {esquinas} {NumeroDe(PosicionBarra.Esquina)}" };
        if (cfg.BarrasPorCaraX > 0) partes.Add($"caras X {cfg.BarrasPorCaraX / 2} + {cfg.BarrasPorCaraX / 2} {NumeroDe(PosicionBarra.CaraX)}");
        if (cfg.BarrasPorCaraY > 0) partes.Add($"caras Y {cfg.BarrasPorCaraY / 2} + {cfg.BarrasPorCaraY / 2} {NumeroDe(PosicionBarra.CaraY)}");
        AgregarNota($"{barras.Count} barras longitudinales: {string.Join(" · ", partes)}", ColorTexto, negrita: true);

        if (sinEstribos)
        {
            AgregarNota("Tramo contra zapata: no lleva estribos (el acero longitudinal sí pasa).", ColorTexto);
        }
        else if (dEstribo is not double de)
        {
            AgregarNota("Elige el tipo de barra del estribo para ver los estribos.", ColorAviso);
        }
        else if (reparto is not null)
        {
            string zonas = string.Join(" · ", reparto.Zonas.Select(z => $"{z.Nombre} {z.Cantidad} @ {z.Espaciado * 100:0.#} cm"));
            AgregarNota($"Estribos del tramo: {reparto.Alturas.Count} {Numero(de)}" + (zonas.Length > 0 ? $" — {zonas}" : ""), ColorTexto);

            List<EstriboMenor> vigentes = piezas.Where(p => !PaqueteEstribos.EstaTapada(p, piezas)).ToList();
            if (vigentes.Count > 0)
            {
                string porForma = string.Join(" · ", vigentes
                    .GroupBy(p => (p.Forma, Numero(diametroPieza(p))))
                    .Select(g => $"{g.Count() * reparto.Alturas.Count} {NombreForma(g.Key.Forma)} {g.Key.Item2}"));
                AgregarNota($"Estribos menores: {porForma}", ColorTexto);
            }

            int tapadas = piezas.Count - vigentes.Count;
            if (tapadas > 0)
                AgregarNota($"{tapadas} grapa(s) quedan dentro de un estribo cerrado y no se generan (en gris).", ColorAviso);

            if (reparto.Aviso is not null) AgregarNota($"Este tramo: {reparto.Aviso}", ColorAviso);
        }
    }

    private static string NombreForma(FormaMenor forma) => forma switch
    {
        FormaMenor.GrapaC => "grapas en C",
        FormaMenor.GrapaS => "grapas en S",
        _ => "estribos cerrados",
    };

    private static string Numero(double diametroMm) =>
        ColoresVarilla.NumeroDe(diametroMm) is int n ? $"#{n}" : $"Ø {diametroMm:0.#} mm";

    // ------------------------------------------------------------ dibujo

    private static Brush Pincel(double diametroMm)
    {
        ColoresVarilla.ColorRgb c = ColoresVarilla.ColorDe(diametroMm);
        return new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
    }

    private void Agregar(UIElement elemento, double x, double y)
    {
        Canvas.SetLeft(elemento, x);
        Canvas.SetTop(elemento, y);
        Lienzo.Children.Add(elemento);
    }

    private void Linea(Point a, Point b, Brush color, double grosor, bool punteada)
    {
        var linea = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = color, StrokeThickness = grosor };
        if (punteada) linea.StrokeDashArray = new DoubleCollection { 3, 2 };
        Lienzo.Children.Add(linea);
    }

    private void Texto(string texto, double x, double y, double tamaño, FontWeight? peso = null) =>
        Agregar(new TextBlock { Text = texto, FontSize = tamaño, Foreground = ColorTexto, FontWeight = peso ?? FontWeights.Normal }, x, y);

    private void AgregarNota(string texto, Brush color, bool negrita = false) =>
        Resumen.Children.Add(new TextBlock
        {
            Text = texto, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = color,
            FontWeight = negrita ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 0, 2),
        });
}
