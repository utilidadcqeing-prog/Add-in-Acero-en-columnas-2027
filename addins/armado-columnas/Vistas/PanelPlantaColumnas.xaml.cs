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
/// Selección múltiple (reunión 10-sep): <b>Ctrl+clic</b> suma columnas a lo
/// que ya haya elegido, y el <b>arrastre</b> selecciona por rectángulo con el
/// mismo criterio que Revit -- hacia la izquierda agarra todo lo que el
/// rectángulo toque, hacia la derecha solo lo que quede completamente adentro.
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
    /// <summary>
    /// Clic en una columna: su <see cref="ColumnaResumen.Id"/>, más si venía
    /// con Ctrl (sumar a lo ya elegido en vez de reemplazarlo).
    /// </summary>
    public event Action<string, bool>? ColumnaClicked;

    /// <summary>
    /// Arrastre: los <see cref="ColumnaResumen.Id"/> que quedaron dentro del
    /// rectángulo, más si venía con Ctrl. A diferencia del clic suelto, esto
    /// no alterna: las deja elegidas.
    /// </summary>
    public event Action<IReadOnlyList<string>, bool>? ColumnasEnRectangulo;

    private static readonly Brush ColorColumna = Paleta.Contorno;
    private static readonly Brush ColorBordeColumna = Paleta.ContornoFuerte;
    private static readonly Brush ColorColumnaActiva = new SolidColorBrush(Paleta.ConAlfa(Paleta.ColorExitoClaro, 120));
    private static readonly Brush ColorBordeActiva = Paleta.ExitoClaro;

    // Azul de "columna elegida": a las elegidas les cae lo que se acepte en el
    // panel del tramo (issue #56). Independiente de si además está activa
    // (verde) o de si ya tiene tramos aceptados (✓).
    private static readonly Brush ColorColumnaMarcada = new SolidColorBrush(Paleta.ConAlfa(Paleta.ColorPrimario, 140));
    private static readonly Brush ColorBordeMarcada = Paleta.Primario;

    private static readonly Brush ColorRejilla = Paleta.Contorno;

    /// <summary>Mismo verde que el chulito de la elevación (VentanaConfiguradorEstribos).</summary>
    private static readonly Brush ColorChulito = Paleta.Exito;

    // Rectángulo de arrastre: azul para "ventana" (izq -> der, solo lo que
    // entra entero) y verde para "cruce" (der -> izq, todo lo que toca),
    // mismos colores que usa Revit.
    private static readonly Brush ColorVentana = Paleta.Primario;
    private static readonly Brush ColorCruce = Paleta.ExitoClaro;

    /// <summary>Un clic que se movió menos que esto sigue siendo un clic, no un arrastre.</summary>
    private const double MinimoArrastre = 4;

    /// <summary>Dónde quedó cada columna dibujada, para resolver clics y arrastres sin hit-test de WPF.</summary>
    private readonly List<(string Id, Rect Caja)> _marcadores = new();

    /// <summary>Lo último que se dibujó, para poder repetirlo cuando cambia el tamaño del panel.</summary>
    private (IReadOnlyList<ColumnaResumen> Columnas,
             IReadOnlyList<Rejilla> Rejillas,
             string? IdActiva,
             IReadOnlySet<string> Elegidas,
             IReadOnlySet<string> Configuradas)? _ultimo;

    private Point _inicioArrastre;
    private Rectangle? _rectanguloArrastre;

    public PanelPlantaColumnas()
    {
        InitializeComponent();

        // El lienzo ya no tiene tamaño fijo: cada vez que cambia, se repite el
        // último dibujo con la escala nueva.
        Lienzo.SizeChanged += (_, _) => Redibujar();

        Lienzo.MouseLeftButtonDown += Lienzo_MouseLeftButtonDown;
        Lienzo.MouseMove += Lienzo_MouseMove;
        Lienzo.MouseLeftButtonUp += Lienzo_MouseLeftButtonUp;
    }

    /// <summary>
    /// Redibuja todo el panel.
    ///
    /// Son tres estados distintos y se ven distinto, porque desde el issue #56
    /// ya no son la misma cosa: <paramref name="idActiva"/> es la columna que
    /// muestra la elevación (verde); <paramref name="idsElegidas"/> son las que
    /// están seleccionadas, o sea a las que les cae lo que se acepte (azul); y
    /// <paramref name="idsConfiguradas"/> son las que tienen al menos un tramo
    /// aceptado, o sea las que se van a generar (✓ verde en la esquina).
    /// </summary>
    public void Cargar(
        IReadOnlyList<ColumnaResumen> columnas, IReadOnlyList<Rejilla> rejillas,
        string? idActiva, IReadOnlySet<string> idsElegidas, IReadOnlySet<string> idsConfiguradas)
    {
        _ultimo = (columnas, rejillas, idActiva, idsElegidas, idsConfiguradas);
        Redibujar();
    }

    private void Redibujar()
    {
        Lienzo.Children.Clear();
        _marcadores.Clear();
        _rectanguloArrastre = null;

        if (_ultimo is not { } estado) return;
        (IReadOnlyList<ColumnaResumen> columnas,
         IReadOnlyList<Rejilla> rejillas,
         string? idActiva,
         IReadOnlySet<string> idsElegidas,
         IReadOnlySet<string> idsConfiguradas) = estado;
        if (columnas.Count == 0 && rejillas.Count == 0) return;

        const double margen = 22;
        double anchoDisponible = Math.Max(1, Lienzo.ActualWidth - margen * 2);
        double altoDisponible = Math.Max(1, Lienzo.ActualHeight - margen * 2);

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
            DibujarColumna(c, Cx(c.X), Cy(c.Y), c.Id == idActiva, idsElegidas.Contains(c.Id), idsConfiguradas.Contains(c.Id));

        // Las etiquetas SIEMPRE después de todos los cuadraditos: así ninguna
        // queda tapada por una columna dibujada más tarde, y el chequeo de
        // encimado (ver el método) ya tiene la lista completa de cuadraditos.
        DibujarEtiquetasDeUbicacion(columnas, Cx, Cy);
    }

    /// <summary>
    /// La marca de ubicación del pilar ("A-1") al lado de cada columna --
    /// reunión del 11-sep: es la coordenada que Revit ya calcula del cruce de
    /// rejillas, no hay que inventar nada, solo dibujarla.
    ///
    /// El 10-sep se habían sacado las etiquetas de la planta justamente porque
    /// con las columnas juntas se encimaban entre sí y tapaban el dibujo. "A-1"
    /// es corto, pero el problema no desaparece solo, así que acá se resuelve
    /// explícito: cada etiqueta se dibuja únicamente si su caja no se pisa con
    /// nada ya dibujado (cuadraditos y etiquetas anteriores). Se prueban dos
    /// posiciones -- debajo y a la derecha -- antes de rendirse. Una columna
    /// sin etiqueta no pierde nada: el nombre sigue en el tooltip y en la
    /// elevación.
    /// </summary>
    private void DibujarEtiquetasDeUbicacion(
        IReadOnlyList<ColumnaResumen> columnas, Func<double, double> cx, Func<double, double> cy)
    {
        const double tam = 12;
        const double aire = 2;

        // Arranca ocupado por todos los cuadraditos, que ya están dibujados.
        List<Rect> ocupado = _marcadores.Select(m => m.Caja).ToList();

        foreach (ColumnaResumen c in columnas)
        {
            if (c.MarcaUbicacion.Length == 0) continue;

            var texto = new TextBlock
            {
                Text = c.MarcaUbicacion,
                FontSize = 10,
                Foreground = Paleta.ContornoOscuro,
                IsHitTestVisible = false,
            };
            texto.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double ancho = texto.DesiredSize.Width;
            double alto = texto.DesiredSize.Height;
            double x = cx(c.X);
            double y = cy(c.Y);

            Rect[] candidatas =
            {
                new(x - ancho / 2, y + tam / 2 + aire, ancho, alto),   // debajo, centrada
                new(x + tam / 2 + aire, y - alto / 2, ancho, alto),    // a la derecha
            };

            if (candidatas.FirstOrDefault(r => !ocupado.Any(o => o.IntersectsWith(r))) is not { Width: > 0 } libre)
                continue;

            Canvas.SetLeft(texto, libre.X);
            Canvas.SetTop(texto, libre.Y);
            Lienzo.Children.Add(texto);
            ocupado.Add(libre);
        }
    }

    private void DibujarIndicadorNorte()
    {
        var texto = new TextBlock
        {
            Text = "N ↑",
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = Paleta.TextoSecundario,
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
            Stroke = Paleta.TextoSecundario,
            StrokeThickness = 1,
            Fill = Paleta.Superficie,
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

    /// <summary>
    /// El cuadradito solo. La etiqueta la pone aparte
    /// <see cref="DibujarEtiquetasDeUbicacion"/>, después de que estén todos
    /// dibujados: el NOMBRE de la columna ("C-12") sigue sin dibujarse -- con
    /// las columnas juntas se encimaba (William, 10-sep) -- y lo que se
    /// etiqueta es la marca de ubicación, que es corta.
    /// </summary>
    private void DibujarColumna(ColumnaResumen c, double x, double y, bool activa, bool elegida, bool configurada)
    {
        const double tam = 12;

        var marcador = new Rectangle
        {
            Width = tam,
            Height = tam,
            Fill = elegida ? ColorColumnaMarcada : (activa ? ColorColumnaActiva : ColorColumna),
            Stroke = activa ? ColorBordeActiva : (elegida ? ColorBordeMarcada : ColorBordeColumna),
            StrokeThickness = activa ? 3 : (elegida ? 2 : 1),
            Cursor = Cursors.Hand,
            ToolTip = c.MarcaUbicacion.Length > 0 ? $"{c.Marca}  ·  {c.MarcaUbicacion}" : c.Marca,
            // Los clics los resuelve el lienzo contra _marcadores: si cada
            // cuadradito se los quedara, un arrastre que empieza encima de uno
            // no llegaría nunca a ser arrastre.
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(marcador, x - tam / 2);
        Canvas.SetTop(marcador, y - tam / 2);
        Lienzo.Children.Add(marcador);

        // El ✓ de "esta columna tiene tramos aceptados, se va a generar" --
        // mismo símbolo y mismo verde que en la elevación (issue #56), para que
        // sea el mismo concepto y no dos marcas distintas que hay que aprender.
        if (configurada)
        {
            var chulito = new TextBlock
            {
                Text = "✓",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = ColorChulito,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(chulito, x + tam / 2 - 1);
            Canvas.SetTop(chulito, y - tam / 2 - 11);
            Lienzo.Children.Add(chulito);
        }

        _marcadores.Add((c.Id, new Rect(x - tam / 2, y - tam / 2, tam, tam)));
    }

    // ------------------------------------------------------------ selección

    private static bool ConCtrl => (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

    private void Lienzo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _inicioArrastre = e.GetPosition(Lienzo);
        Lienzo.CaptureMouse();
    }

    private void Lienzo_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !Lienzo.IsMouseCaptured) return;

        Point actual = e.GetPosition(Lienzo);
        if (Math.Abs(actual.X - _inicioArrastre.X) < MinimoArrastre &&
            Math.Abs(actual.Y - _inicioArrastre.Y) < MinimoArrastre) return;

        bool haciaLaDerecha = actual.X >= _inicioArrastre.X;

        if (_rectanguloArrastre is null)
        {
            _rectanguloArrastre = new Rectangle { StrokeThickness = 1, IsHitTestVisible = false };
            Lienzo.Children.Add(_rectanguloArrastre);
        }

        // Ventana (izq -> der): línea llena. Cruce (der -> izq): punteada.
        _rectanguloArrastre.Stroke = haciaLaDerecha ? ColorVentana : ColorCruce;
        _rectanguloArrastre.Fill = new SolidColorBrush(Paleta.ConAlfa(haciaLaDerecha ? Paleta.ColorPrimario : Paleta.ColorExitoClaro, 30));
        _rectanguloArrastre.StrokeDashArray = haciaLaDerecha ? null : new DoubleCollection { 4, 3 };

        Rect caja = new(_inicioArrastre, actual);
        _rectanguloArrastre.Width = caja.Width;
        _rectanguloArrastre.Height = caja.Height;
        Canvas.SetLeft(_rectanguloArrastre, caja.X);
        Canvas.SetTop(_rectanguloArrastre, caja.Y);
    }

    private void Lienzo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!Lienzo.IsMouseCaptured) return;
        Lienzo.ReleaseMouseCapture();

        Point fin = e.GetPosition(Lienzo);
        bool huboArrastre = _rectanguloArrastre is not null;

        if (_rectanguloArrastre is not null)
        {
            Lienzo.Children.Remove(_rectanguloArrastre);
            _rectanguloArrastre = null;
        }

        if (!huboArrastre)
        {
            // Clic suelto: la columna que esté bajo el cursor, si hay alguna.
            foreach ((string id, Rect caja) in _marcadores)
            {
                if (!caja.Contains(fin)) continue;
                ColumnaClicked?.Invoke(id, ConCtrl);
                return;
            }
            return;
        }

        // Criterio de Revit: arrastrando hacia la DERECHA solo entra lo que
        // queda completamente adentro ("ventana"); hacia la IZQUIERDA entra
        // todo lo que el rectángulo toque ("cruce").
        bool haciaLaDerecha = fin.X >= _inicioArrastre.X;
        Rect seleccion = new(_inicioArrastre, fin);

        List<string> alcanzadas = _marcadores
            .Where(m => haciaLaDerecha ? seleccion.Contains(m.Caja) : seleccion.IntersectsWith(m.Caja))
            .Select(m => m.Id)
            .ToList();

        if (alcanzadas.Count > 0) ColumnasEnRectangulo?.Invoke(alcanzadas, ConCtrl);
    }
}
