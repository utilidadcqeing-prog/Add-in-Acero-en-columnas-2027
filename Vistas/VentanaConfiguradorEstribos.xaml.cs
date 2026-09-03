namespace practicacq.Vistas;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using practicacq.Modelo;

/// <summary>
/// L2: "Ventana Configurador de Estribos". Trabaja entera contra
/// <see cref="IProveedorContexto"/>/<see cref="IProveedorTipos"/> — no hay una
/// sola línea de <c>Autodesk.Revit</c> acá adentro, a propósito (ver el
/// contrato de L1). Quien la abre decide si le pasa los proveedores falsos
/// (para probar sin Revit) o los reales (J2/J-tipos, cuando existan).
///
/// El panel de la derecha implementa también L3 (máquina de estados: modos
/// excluyentes, autocompletado del espaciado no confinado, validación por
/// campo con mensaje concreto) — quedó todo en el mismo archivo porque L2 y
/// L3 comparten el mismo panel, no dos controles separados.
/// </summary>
public partial class VentanaConfiguradorEstribos : Window
{
    private readonly IProveedorContexto _contexto;
    private readonly IProveedorTipos _tipos;

    /// <summary>
    /// Una copia de <see cref="ConfigTramo"/> por (columna, tramo). Es el
    /// germen de la persistencia que formaliza L5 ("D1 — ida y vuelta sin
    /// perder nada"): alcanza para que navegar con la lista o las flechas no
    /// pierda lo que se tecleó, y cada tramo guarda su propia copia (no una
    /// referencia compartida) incluso cuando llegó por "Aplicar a selección".
    /// </summary>
    private readonly Dictionary<(string ColumnaId, int Indice), ConfigTramo> _configPorTramo = new();

    /// <summary>
    /// Una copia de <see cref="ConfigLongitudinal"/> por columna (no por
    /// tramo: el acero longitudinal corre toda la altura de la columna). L4
    /// vive del lado de "EstribosMenores" dentro de este mismo tipo.
    /// </summary>
    private readonly Dictionary<string, ConfigLongitudinal> _longitudinalPorColumna = new();

    private readonly ObservableCollection<NodoLista> _nodos = new();

    private ColumnaContexto? _columnaActiva;
    private int _tramoActivoIndice;
    private string? _ultimoColumnaIdActivada;

    /// <summary>
    /// Se dispara con el id (el mismo texto que recibe <see cref="IProveedorContexto.Leer"/>)
    /// cada vez que cambia la columna activa. Sin una sola línea de
    /// <c>Autodesk.Revit</c> acá adentro -- quien abre la ventana (ComandoEstribos)
    /// lo engancha para seleccionar el elemento real en Revit (issue #24):
    /// selección liviana nada más, nunca abrir la familia.
    /// </summary>
    public event Action<string>? ColumnaActivada;

    public VentanaConfiguradorEstribos(IProveedorContexto contexto, IProveedorTipos tipos, bool modoSimulado = false)
    {
        InitializeComponent();

        _contexto = contexto;
        _tipos = tipos;

        BannerSimulado.Visibility = modoSimulado ? Visibility.Visible : Visibility.Collapsed;

        ListaSeleccion.ItemsSource = _nodos;
        ComboTipoBarra.ItemsSource = _tipos.ListarTiposBarra();
        ComboTipoGancho.ItemsSource = _tipos.ListarTiposGancho();
        ComboBarraCaraX.ItemsSource = _tipos.ListarTiposBarra();
        ComboBarraCaraY.ItemsSource = _tipos.ListarTiposBarra();
        ComboBarraEsquinas.ItemsSource = _tipos.ListarTiposBarra();

        // Pares del 2 al 20 -- "nada se teclea a mano" aplica también acá,
        // no solo a los tipos de barra.
        List<int> paresHasta20 = Enumerable.Range(1, 10).Select(n => n * 2).ToList();
        ComboBarrasX.ItemsSource = paresHasta20;
        ComboBarrasY.ItemsSource = paresHasta20;
        ComboBarrasX.SelectedItem = 2;
        ComboBarrasY.SelectedItem = 2;

        RadioUniforme.Checked += (_, _) => { ActualizarVisibilidadModo(); RevalidarPanel(); };
        RadioConfinado.Checked += (_, _) => { ActualizarVisibilidadModo(); RevalidarPanel(); };

        // Cableado acá y no en el XAML: si estos se disparan durante el
        // parseo inicial (InitializeComponent, al asignar los Text="..." del
        // propio XAML), los controles que tocan todavía no existen como
        // campos conectados y esto revienta con NullReferenceException --
        // pasó de verdad con CampoEspaciadoConfinado_TextChanged, ver commit.
        CampoEspaciadoConfinado.TextChanged += CampoEspaciadoConfinado_TextChanged;
        CampoEspaciadoConfinado.TextChanged += (_, _) => RevalidarPanel();
        CampoEspaciadoUniforme.TextChanged += (_, _) => RevalidarPanel();
        CampoL1.TextChanged += (_, _) => RevalidarPanel();
        CampoEspaciadoNoConfinado.TextChanged += (_, _) => RevalidarPanel();
        ComboTipoBarra.SelectionChanged += (_, _) => RevalidarPanel();
        ComboTipoGancho.SelectionChanged += (_, _) => RevalidarPanel();

        CargarColumnas();
    }

    // -------------------------------------------------------- lista / árbol

    private void CargarColumnas()
    {
        _nodos.Clear();

        foreach (ColumnaResumen resumen in _contexto.ListarColumnas())
        {
            ColumnaContexto columna = _contexto.Leer(resumen.Id);

            _nodos.Add(new NodoLista
            {
                EsColumna = true,
                ColumnaId = resumen.Id,
                Etiqueta = $"{resumen.Marca}  ({columna.Tramos.Count} tramos)",
            });

            foreach (Tramo tramo in columna.Tramos)
            {
                _nodos.Add(new NodoLista
                {
                    EsColumna = false,
                    ColumnaId = resumen.Id,
                    TramoIndice = tramo.Indice,
                    Etiqueta = $"     Tramo {tramo.Indice}  ·  {tramo.Altura:0.00} m  ·  {Rotulo(tramo.Tipo)}",
                });
            }
        }

        NodoLista? primerTramo = _nodos.FirstOrDefault(n => !n.EsColumna);
        if (primerTramo is not null) Activar(primerTramo);
    }

    private static string Rotulo(TipoTramo tipo) => tipo switch
    {
        TipoTramo.Libre => "libre",
        TipoTramo.DentroDeViga => "nudo",
        TipoTramo.ContraZapata => "contra zapata",
        _ => tipo.ToString(),
    };

    private void ListaSeleccion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListaSeleccion.SelectedItem is NodoLista nodo) Activar(nodo);
    }

    /// <summary>
    /// Tildar/destildar una columna cascadea a todos sus tramos. Tildar un
    /// tramo suelto no toca a los demás ni a su columna — los dos niveles de
    /// selección son independientes, como quedó confirmado en el issue.
    /// </summary>
    private void CheckBoxNodo_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: NodoLista nodo } || !nodo.EsColumna) return;

        foreach (NodoLista tramo in _nodos.Where(n => !n.EsColumna && n.ColumnaId == nodo.ColumnaId))
            tramo.Marcado = nodo.Marcado;
    }

    // ------------------------------------------------------- tramo activo

    private void Activar(NodoLista nodo)
    {
        GuardarPanelEnTramoActivo();
        GuardarLongitudinalDeColumnaActiva();

        bool cambioDeColumna = _columnaActiva is null || nodo.ColumnaId != _ultimoColumnaIdActivada;

        _columnaActiva = _contexto.Leer(nodo.ColumnaId);
        _tramoActivoIndice = nodo.EsColumna ? 0 : nodo.TramoIndice;

        RedibujarElevacion();
        CargarPanelDelTramoActivo();
        CargarLongitudinalDeColumnaActiva();

        // Solo re-selecciona en Revit si de verdad cambió la columna -- pasar
        // de un tramo a otro de la misma columna no necesita tocar la
        // selección, porque los tramos no son elementos separados en Revit.
        if (cambioDeColumna)
        {
            _ultimoColumnaIdActivada = nodo.ColumnaId;
            ColumnaActivada?.Invoke(nodo.ColumnaId);
        }
    }

    private void IrA(int indice)
    {
        if (_columnaActiva is null) return;

        GuardarPanelEnTramoActivo();
        _tramoActivoIndice = Math.Clamp(indice, 0, _columnaActiva.Tramos.Count - 1);
        RedibujarElevacion();
        CargarPanelDelTramoActivo();
    }

    private void BotonAnterior_Click(object sender, RoutedEventArgs e) => IrA(_tramoActivoIndice - 1);

    private void BotonSiguiente_Click(object sender, RoutedEventArgs e) => IrA(_tramoActivoIndice + 1);

    // --------------------------------------------------------------- panel

    private void ActualizarVisibilidadModo()
    {
        bool confinado = RadioConfinado.IsChecked == true;
        PanelUniforme.Visibility = confinado ? Visibility.Collapsed : Visibility.Visible;
        PanelConfinado.Visibility = confinado ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// L3: valida el <see cref="ConfigTramo"/> del panel campo por campo, con
    /// mensaje concreto -- nunca "valor inválido" a secas. Si se pasa el
    /// tramo activo, suma el chequeo de norma (L1 ≥ altura/2 no deja zona
    /// central); sin tramo (por ejemplo al validar la plantilla de "aplicar a
    /// selección", donde la altura depende de a cuáles se les vaya a aplicar)
    /// se valida solo lo que no depende de un tramo puntual.
    /// </summary>
    private static List<string> Validar(ConfigTramo cfg, Tramo? tramo)
    {
        var errores = new List<string>();

        if (string.IsNullOrWhiteSpace(cfg.TipoBarraNombre))
            errores.Add("Elegí un tipo de barra.");
        if (string.IsNullOrWhiteSpace(cfg.TipoGanchoNombre))
            errores.Add("Elegí un tipo de gancho.");

        if (cfg.Modo == ModoEstribo.Uniforme)
        {
            if (cfg.EspaciadoUniforme <= 0)
                errores.Add("El espaciado tiene que ser mayor que cero.");
        }
        else
        {
            if (cfg.LongitudConfinamiento <= 0)
                errores.Add("La longitud de confinamiento (L1) tiene que ser mayor que cero.");
            if (cfg.EspaciadoConfinado <= 0)
                errores.Add("El espaciado confinado tiene que ser mayor que cero.");
            if (cfg.EspaciadoNoConfinado <= 0)
                errores.Add("El espaciado no confinado tiene que ser mayor que cero.");

            if (tramo is not null && tramo.Altura > 0 && cfg.LongitudConfinamiento * 2 >= tramo.Altura)
                errores.Add(
                    $"L1 ({cfg.LongitudConfinamiento:0.00} m) x2 ≥ la altura del tramo " +
                    $"({tramo.Altura:0.00} m): no queda zona central.");

            if (tramo is { Tipo: TipoTramo.DentroDeViga })
                errores.Add(
                    "Este tramo ya es un nudo (dentro de viga): el confinamiento normalmente " +
                    "se configura en los tramos libres adyacentes, no acá.");
        }

        return errores;
    }

    /// <summary>Revalida el panel contra el tramo activo y actualiza el mensaje en pantalla.</summary>
    private void RevalidarPanel()
    {
        if (_columnaActiva is null) { TextoValidacion.Text = ""; return; }

        Tramo tramo = _columnaActiva.Tramos[_tramoActivoIndice];
        List<string> errores = Validar(LeerConfigDelPanel(), tramo);
        TextoValidacion.Text = string.Join(Environment.NewLine, errores);
    }

    /// <summary>
    /// Autocompleta el espaciado no confinado al doble del confinado. Queda
    /// editable después: si el usuario lo toca, esto no lo vuelve a pisar en
    /// esta misma edición (la máquina de estados completa que decide cuándo
    /// re-sugerir es trabajo de L3).
    /// </summary>
    private void CampoEspaciadoConfinado_TextChanged(object sender, TextChangedEventArgs e)
    {
        double confinado = LeerDouble(CampoEspaciadoConfinado.Text);
        CampoEspaciadoNoConfinado.Text = (confinado * 2).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private ConfigTramo LeerConfigDelPanel() => new()
    {
        Modo = RadioConfinado.IsChecked == true ? ModoEstribo.Confinado : ModoEstribo.Uniforme,
        TipoBarraNombre = ComboTipoBarra.SelectedItem as string,
        TipoGanchoNombre = ComboTipoGancho.SelectedItem as string,
        EspaciadoUniforme = LeerDouble(CampoEspaciadoUniforme.Text),
        LongitudConfinamiento = LeerDouble(CampoL1.Text),
        EspaciadoConfinado = LeerDouble(CampoEspaciadoConfinado.Text),
        EspaciadoNoConfinado = LeerDouble(CampoEspaciadoNoConfinado.Text),
    };

    private void MostrarConfigEnPanel(ConfigTramo cfg)
    {
        RadioUniforme.IsChecked = cfg.Modo == ModoEstribo.Uniforme;
        RadioConfinado.IsChecked = cfg.Modo == ModoEstribo.Confinado;
        ActualizarVisibilidadModo();

        ComboTipoBarra.SelectedItem = cfg.TipoBarraNombre;
        ComboTipoGancho.SelectedItem = cfg.TipoGanchoNombre;

        CampoEspaciadoUniforme.Text = Formato(cfg.EspaciadoUniforme);
        CampoL1.Text = Formato(cfg.LongitudConfinamiento);
        CampoEspaciadoConfinado.Text = Formato(cfg.EspaciadoConfinado);
        CampoEspaciadoNoConfinado.Text = Formato(cfg.EspaciadoNoConfinado);
    }

    private static ConfigTramo ConfigPorDefecto(IProveedorTipos tipos) => new()
    {
        TipoBarraNombre = tipos.ListarTiposBarra().FirstOrDefault(),
        TipoGanchoNombre = tipos.ListarTiposGancho().FirstOrDefault(),
        EspaciadoUniforme = 0.20,
        LongitudConfinamiento = 0.50,
        EspaciadoConfinado = 0.07,
        EspaciadoNoConfinado = 0.14,
    };

    private void GuardarPanelEnTramoActivo()
    {
        if (_columnaActiva is null) return;

        var clave = (_columnaActiva.Marca, _tramoActivoIndice);
        _configPorTramo[clave] = LeerConfigDelPanel();
    }

    private void CargarPanelDelTramoActivo()
    {
        if (_columnaActiva is null) return;

        var clave = (_columnaActiva.Marca, _tramoActivoIndice);
        ConfigTramo cfg = _configPorTramo.TryGetValue(clave, out ConfigTramo? existente)
            ? existente
            : ConfigPorDefecto(_tipos);

        MostrarConfigEnPanel(cfg);

        Tramo tramo = _columnaActiva.Tramos[_tramoActivoIndice];
        TextoTramoActivo.Text = $"{_columnaActiva.Marca} · {_tramoActivoIndice + 1}/{_columnaActiva.Tramos.Count} · {Rotulo(tramo.Tipo)}";

        RevalidarPanel();
    }

    // -------------------------------------------------- acero longitudinal (L4)

    private ConfigLongitudinal LeerLongitudinalDelPanel() => new()
    {
        BarrasPorCaraX = ComboBarrasX.SelectedItem as int? ?? 2,
        BarrasPorCaraY = ComboBarrasY.SelectedItem as int? ?? 2,
        TipoBarraCaraXNombre = ComboBarraCaraX.SelectedItem as string,
        TipoBarraCaraYNombre = ComboBarraCaraY.SelectedItem as string,
        TipoBarraEsquinasNombre = ComboBarraEsquinas.SelectedItem as string,
        // Las posiciones de grapas/cajas no se leen del panel: se editan aparte
        // en L4 y viven en _longitudinalPorColumna hasta que este método las
        // pisa -- por eso Guardar... las vuelve a copiar explícitamente.
    };

    private void MostrarLongitudinalEnPanel(ConfigLongitudinal cfg)
    {
        ComboBarrasX.SelectedItem = cfg.BarrasPorCaraX;
        ComboBarrasY.SelectedItem = cfg.BarrasPorCaraY;
        ComboBarraCaraX.SelectedItem = cfg.TipoBarraCaraXNombre;
        ComboBarraCaraY.SelectedItem = cfg.TipoBarraCaraYNombre;
        ComboBarraEsquinas.SelectedItem = cfg.TipoBarraEsquinasNombre;

        TextoEstribosMenores.Text = cfg.EstribosMenores.Count == 0
            ? "Sin estribos menores configurados."
            : $"{cfg.EstribosMenores.Count} estribo(s) menor(es) configurado(s).";
    }

    private static ConfigLongitudinal ConfigLongitudinalPorDefecto(IProveedorTipos tipos) => new()
    {
        BarrasPorCaraX = 2,
        BarrasPorCaraY = 2,
        TipoBarraCaraXNombre = tipos.ListarTiposBarra().FirstOrDefault(),
        TipoBarraCaraYNombre = tipos.ListarTiposBarra().FirstOrDefault(),
        TipoBarraEsquinasNombre = tipos.ListarTiposBarra().FirstOrDefault(),
    };

    private void GuardarLongitudinalDeColumnaActiva()
    {
        if (_columnaActiva is null) return;

        // Las posiciones de grapas/cajas ya editadas en L4 (si las hay) se
        // preservan -- este panel no las toca, solo cantidad/tipo de barra.
        List<EstriboMenor> existentes = _longitudinalPorColumna.TryGetValue(_columnaActiva.Marca, out ConfigLongitudinal? previo)
            ? previo.EstribosMenores
            : new List<EstriboMenor>();

        ConfigLongitudinal nuevo = LeerLongitudinalDelPanel();
        nuevo.EstribosMenores = existentes;
        _longitudinalPorColumna[_columnaActiva.Marca] = nuevo;
    }

    private void CargarLongitudinalDeColumnaActiva()
    {
        if (_columnaActiva is null) return;

        ConfigLongitudinal cfg = _longitudinalPorColumna.TryGetValue(_columnaActiva.Marca, out ConfigLongitudinal? existente)
            ? existente
            : ConfigLongitudinalPorDefecto(_tipos);

        MostrarLongitudinalEnPanel(cfg);
    }

    private void BotonEstribosMenores_Click(object sender, RoutedEventArgs e)
    {
        if (_columnaActiva is null) return;

        int barrasX = ComboBarrasX.SelectedItem as int? ?? 0;
        int barrasY = ComboBarrasY.SelectedItem as int? ?? 0;

        if (barrasX <= 0 || barrasY <= 0)
        {
            TextoEstado.Text = "Elegí N. barras X y N. barras Y antes de editar los estribos menores.";
            return;
        }

        List<EstriboMenor> existentes = _longitudinalPorColumna.TryGetValue(_columnaActiva.Marca, out ConfigLongitudinal? actual)
            ? actual.EstribosMenores
            : new List<EstriboMenor>();

        var dialogo = new VentanaEstribosMenores(barrasX, barrasY, existentes) { Owner = this };
        if (dialogo.ShowDialog() != true) return;

        GuardarLongitudinalDeColumnaActiva();
        _longitudinalPorColumna[_columnaActiva.Marca].EstribosMenores = dialogo.Resultado;

        TextoEstribosMenores.Text = dialogo.Resultado.Count == 0
            ? "Sin estribos menores configurados."
            : $"{dialogo.Resultado.Count} estribo(s) menor(es) configurado(s).";
    }

    // ----------------------------------------------------------- selección

    private void BotonAplicarSeleccion_Click(object sender, RoutedEventArgs e)
    {
        ConfigTramo plantilla = LeerConfigDelPanel();

        // Validación de campos (sin altura -- eso depende de a qué tramo se
        // le aplique, se chequea abajo, por tramo, una vez que se sabe).
        List<string> erroresPlantilla = Validar(plantilla, tramo: null);
        if (erroresPlantilla.Count > 0)
        {
            TextoEstado.Text = "No se aplicó nada: " + string.Join(" ", erroresPlantilla);
            return;
        }

        List<NodoLista> objetivo = _nodos.Where(n => !n.EsColumna && n.Marcado).ToList();
        if (objetivo.Count == 0)
        {
            TextoEstado.Text = "No hay tramos tildados en la lista: tildá alguno (o una columna entera) para aplicarles esta configuración.";
            return;
        }

        var conProblemaDeNorma = new List<string>();

        foreach (NodoLista nodo in objetivo)
        {
            // Copia independiente por tramo -- no una referencia compartida.
            // Editar uno solo después (con las flechas) no le tiene que
            // cambiar el valor a los demás que recibieron esta misma config.
            _configPorTramo[(nodo.ColumnaId, nodo.TramoIndice)] = new ConfigTramo
            {
                Modo = plantilla.Modo,
                TipoBarraNombre = plantilla.TipoBarraNombre,
                TipoGanchoNombre = plantilla.TipoGanchoNombre,
                EspaciadoUniforme = plantilla.EspaciadoUniforme,
                LongitudConfinamiento = plantilla.LongitudConfinamiento,
                EspaciadoConfinado = plantilla.EspaciadoConfinado,
                EspaciadoNoConfinado = plantilla.EspaciadoNoConfinado,
            };

            // El chequeo de L1 vs altura sí depende de CADA tramo -- distintos
            // tramos de la selección pueden tener distinta altura. Se avisa
            // pero no se bloquea: es la misma filosofía del resto del código
            // (avisos, no excepciones), la geometría incoherente de verdad la
            // ataja el generador (J3), esto es una alerta temprana en la UI.
            ColumnaContexto contexto = _contexto.Leer(nodo.ColumnaId);
            Tramo tramoReal = contexto.Tramos[nodo.TramoIndice];
            if (plantilla.Modo == ModoEstribo.Confinado
                && tramoReal.Altura > 0
                && plantilla.LongitudConfinamiento * 2 >= tramoReal.Altura)
            {
                conProblemaDeNorma.Add($"{contexto.Marca}[{nodo.TramoIndice}]");
            }
        }

        TextoEstado.Text = conProblemaDeNorma.Count == 0
            ? $"Aplicado a {objetivo.Count} tramo(s)."
            : $"Aplicado a {objetivo.Count} tramo(s). Atención: en {string.Join(", ", conProblemaDeNorma)} " +
              "L1 x2 ≥ la altura del tramo -- revisá esos, no queda zona central.";

        // Si el tramo activo estaba entre los que se acaban de tocar, refleja
        // el resultado en el panel en vez de dejarlo desactualizado.
        if (_columnaActiva is not null
            && objetivo.Any(n => n.ColumnaId == _columnaActiva.Marca && n.TramoIndice == _tramoActivoIndice))
        {
            CargarPanelDelTramoActivo();
        }
    }

    // -------------------------------------------------------------- dibujo

    /// <summary>
    /// Dibuja la columna entera como un contorno continuo (no bloques
    /// sueltos), con los niveles a la izquierda unidos por una línea guía
    /// punteada -- mismo lenguaje visual que usa la ventana de referencia de
    /// William. Las bandas de color se superponen al contorno donde
    /// corresponde: nudo/zapata cruzando más ancho que la columna (como lo
    /// hace una viga real), el tramo activo resaltado en verde, y los tramos
    /// que ya tienen una configuración guardada en esta sesión con una trama
    /// rayada (todavía no hay geometría real creada en Revit -- esa marca es
    /// de "configurado en la UI", no de "generado en el modelo").
    /// </summary>
    private void RedibujarElevacion()
    {
        CanvasElevacion.Children.Clear();
        if (_columnaActiva is null) return;

        const double margenSuperior = 24;
        const double margenInferior = 24;
        const double anchoColumna = 46;
        const double xColumna = 172;
        const double sobresaleNudo = 16;
        const double anchoEtiqueta = 130;
        const double xLeaderIni = 138;
        double xLeaderFin = xColumna - 4;

        double zMin = _columnaActiva.ZInferior;
        double zMax = _columnaActiva.ZSuperior;
        foreach (NivelRotulo nivel in _columnaActiva.Niveles)
        {
            zMin = Math.Min(zMin, nivel.Cota);
            zMax = Math.Max(zMax, nivel.Cota);
        }

        double alturaDisponible = Math.Max(1, CanvasElevacion.Height - margenSuperior - margenInferior);
        double rango = Math.Max(0.01, zMax - zMin);
        double escala = alturaDisponible / rango;
        double Y(double cota) => margenSuperior + (zMax - cota) * escala;

        // 1) Contorno continuo de la columna, de punta a punta.
        var contorno = new Rectangle
        {
            Width = anchoColumna,
            Height = Math.Max(1, Y(_columnaActiva.ZInferior) - Y(_columnaActiva.ZSuperior)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            StrokeThickness = 1.5,
            Fill = Brushes.White,
        };
        Canvas.SetLeft(contorno, xColumna);
        Canvas.SetTop(contorno, Y(_columnaActiva.ZSuperior));
        CanvasElevacion.Children.Add(contorno);

        // 2) Rótulos de nivel con línea guía punteada hasta la columna.
        foreach (NivelRotulo nivel in _columnaActiva.Niveles)
        {
            double y = Y(nivel.Cota);

            var linea = new Line
            {
                X1 = xLeaderIni,
                Y1 = y,
                X2 = xLeaderFin,
                Y2 = y,
                Stroke = Brushes.LightGray,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 1, 2 },
                IsHitTestVisible = false,
            };
            CanvasElevacion.Children.Add(linea);

            var texto = new TextBlock
            {
                Text = $"N{(nivel.Cota >= 0 ? "+" : "")}{nivel.Cota:0.00}  {nivel.Nombre}",
                FontSize = 10,
                Foreground = Brushes.DimGray,
                TextAlignment = TextAlignment.Right,
                Width = anchoEtiqueta,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(texto, 0);
            Canvas.SetTop(texto, y - 7);
            CanvasElevacion.Children.Add(texto);
        }

        // 3) Bandas por tramo, superpuestas al contorno.
        for (int i = 0; i < _columnaActiva.Tramos.Count; i++)
        {
            Tramo tramo = _columnaActiva.Tramos[i];
            double yTop = Y(tramo.ZSuperior);
            double alto = Math.Max(1, Y(tramo.ZInferior) - yTop);
            bool activo = i == _tramoActivoIndice;
            bool configurado = _configPorTramo.ContainsKey((_columnaActiva.Marca, i));

            if (tramo.Tipo is TipoTramo.DentroDeViga or TipoTramo.ContraZapata)
            {
                Color color = tramo.Tipo == TipoTramo.DentroDeViga
                    ? Color.FromRgb(0x3B, 0x82, 0xF6)
                    : Color.FromRgb(0xB4, 0x53, 0x09);

                var banda = new Rectangle
                {
                    Width = anchoColumna + sobresaleNudo * 2,
                    Height = alto,
                    Fill = new SolidColorBrush(color),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(banda, xColumna - sobresaleNudo);
                Canvas.SetTop(banda, yTop);
                CanvasElevacion.Children.Add(banda);
            }
            else if (configurado)
            {
                var relleno = new Rectangle
                {
                    Width = anchoColumna,
                    Height = alto,
                    Fill = TramaConfigurado,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(relleno, xColumna);
                Canvas.SetTop(relleno, yTop);
                CanvasElevacion.Children.Add(relleno);
            }

            if (activo)
            {
                var resalte = new Rectangle
                {
                    Width = anchoColumna + 6,
                    Height = alto,
                    Fill = new SolidColorBrush(Color.FromArgb(60, 0x22, 0xC5, 0x5E)),
                    Stroke = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
                    StrokeThickness = 2,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(resalte, xColumna - 3);
                Canvas.SetTop(resalte, yTop);
                CanvasElevacion.Children.Add(resalte);
            }
            else
            {
                // Zona transparente clickeable: tocar cualquier tramo navega ahí.
                var zonaClick = new Rectangle
                {
                    Width = anchoColumna + sobresaleNudo * 2,
                    Height = alto,
                    Fill = Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                Canvas.SetLeft(zonaClick, xColumna - sobresaleNudo);
                Canvas.SetTop(zonaClick, yTop);
                int indiceCapturado = i;
                zonaClick.MouseLeftButtonUp += (_, _) => IrA(indiceCapturado);
                CanvasElevacion.Children.Add(zonaClick);
            }
        }
    }

    /// <summary>Trama rayada 45° para marcar un tramo con configuración ya guardada.</summary>
    private static readonly Brush TramaConfigurado = CrearTramaConfigurado();

    private static Brush CrearTramaConfigurado()
    {
        var celda = new Canvas { Width = 6, Height = 6 };
        celda.Children.Add(new Rectangle { Width = 6, Height = 6, Fill = Brushes.White });
        celda.Children.Add(new Line
        {
            X1 = 0,
            Y1 = 6,
            X2 = 6,
            Y2 = 0,
            Stroke = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            StrokeThickness = 1,
        });

        return new VisualBrush(celda)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 6, 6),
            ViewportUnits = BrushMappingMode.Absolute,
        };
    }

    // ---------------------------------------------------------- exportar

    /// <summary>
    /// Arma el PlanArmado completo a partir del estado en memoria -- separado
    /// de <see cref="BotonExportar_Click"/> para poder probarlo sin disparar
    /// el SaveFileDialog real (L5).
    /// </summary>
    private PlanArmado ConstruirPlanArmado()
    {
        GuardarPanelEnTramoActivo();
        GuardarLongitudinalDeColumnaActiva();

        var plan = new PlanArmado();

        foreach (string id in _nodos.Where(n => n.EsColumna).Select(n => n.ColumnaId))
        {
            ColumnaContexto contexto = _contexto.Leer(id);
            var tramos = new List<ConfigTramo>();

            for (int i = 0; i < contexto.Tramos.Count; i++)
            {
                tramos.Add(_configPorTramo.TryGetValue((id, i), out ConfigTramo? cfg) ? cfg : ConfigPorDefecto(_tipos));
            }

            ConfigLongitudinal longitudinal = _longitudinalPorColumna.TryGetValue(id, out ConfigLongitudinal? l)
                ? l
                : ConfigLongitudinalPorDefecto(_tipos);

            plan.Columnas.Add(new ColumnaPlan { Id = id, Contexto = contexto, Tramos = tramos, Longitudinal = longitudinal });
        }

        return plan;
    }

    private void BotonExportar_Click(object sender, RoutedEventArgs e)
    {
        PlanArmado plan = ConstruirPlanArmado();

        var dialogo = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "PlanArmado.json" };
        if (dialogo.ShowDialog() != true) return;

        string json = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dialogo.FileName, json);

        TextoEstado.Text = $"Exportado: {dialogo.FileName}";
    }

    private static double LeerDouble(string texto) =>
        double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out double valor) ? valor : 0.0;

    private static string Formato(double valor) => valor.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// Fila de <see cref="ListaSeleccion"/> — una columna o uno de sus tramos.
/// Vive acá y no en Modelo/ porque es puro estado de presentación (el
/// checkbox tildado), no algo que tenga sentido serializar en un PlanArmado.
/// </summary>
public sealed class NodoLista : INotifyPropertyChanged
{
    public bool EsColumna { get; init; }
    public string ColumnaId { get; init; } = "";
    public int TramoIndice { get; init; }
    public string Etiqueta { get; init; } = "";

    private bool _marcado;
    public bool Marcado
    {
        get => _marcado;
        set
        {
            if (_marcado == value) return;
            _marcado = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Marcado)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
