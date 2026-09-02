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

    private readonly ObservableCollection<NodoLista> _nodos = new();

    private ColumnaContexto? _columnaActiva;
    private int _tramoActivoIndice;

    public VentanaConfiguradorEstribos(IProveedorContexto contexto, IProveedorTipos tipos, bool modoSimulado = false)
    {
        InitializeComponent();

        _contexto = contexto;
        _tipos = tipos;

        BannerSimulado.Visibility = modoSimulado ? Visibility.Visible : Visibility.Collapsed;

        ListaSeleccion.ItemsSource = _nodos;
        ComboTipoBarra.ItemsSource = _tipos.ListarTiposBarra();
        ComboTipoGancho.ItemsSource = _tipos.ListarTiposGancho();

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

        _columnaActiva = _contexto.Leer(nodo.ColumnaId);
        _tramoActivoIndice = nodo.EsColumna ? 0 : nodo.TramoIndice;

        RedibujarElevacion();
        CargarPanelDelTramoActivo();
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

    private void RedibujarElevacion()
    {
        CanvasElevacion.Children.Clear();
        if (_columnaActiva is null) return;

        double alturaTotal = _columnaActiva.ZSuperior - _columnaActiva.ZInferior;
        if (alturaTotal <= 0) return;

        double alturaCanvas = CanvasElevacion.Height;
        const double anchoBanda = 90;
        double escala = alturaCanvas / alturaTotal;

        for (int i = 0; i < _columnaActiva.Tramos.Count; i++)
        {
            Tramo tramo = _columnaActiva.Tramos[i];
            double y0 = alturaCanvas - (tramo.ZSuperior - _columnaActiva.ZInferior) * escala;
            double alto = Math.Max(2, tramo.Altura * escala - 1);

            Color colorTipo = tramo.Tipo switch
            {
                TipoTramo.DentroDeViga => Color.FromRgb(0x3B, 0x82, 0xF6),   // azul: nudo
                TipoTramo.ContraZapata => Color.FromRgb(0xB4, 0x53, 0x09),   // marrón: zapata
                _ => Color.FromRgb(0xE5, 0xE7, 0xEB),                       // gris: libre
            };

            bool activo = i == _tramoActivoIndice;

            var rect = new Rectangle
            {
                Width = anchoBanda,
                Height = alto,
                Fill = new SolidColorBrush(colorTipo),
                Stroke = activo ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)) : Brushes.White,
                StrokeThickness = activo ? 3 : 1,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            int indiceCapturado = i;
            rect.MouseLeftButtonUp += (_, _) => IrA(indiceCapturado);

            Canvas.SetLeft(rect, 10);
            Canvas.SetTop(rect, y0);
            CanvasElevacion.Children.Add(rect);

            if (alto > 13)
            {
                var texto = new TextBlock
                {
                    Text = tramo.Indice.ToString(),
                    FontSize = 10,
                    Foreground = activo ? Brushes.White : Brushes.Black,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(texto, 10 + anchoBanda / 2 - 4);
                Canvas.SetTop(texto, y0 + alto / 2 - 7);
                CanvasElevacion.Children.Add(texto);
            }
        }
    }

    // ---------------------------------------------------------- exportar

    private void BotonExportar_Click(object sender, RoutedEventArgs e)
    {
        GuardarPanelEnTramoActivo();

        var plan = new PlanArmado();

        foreach (string id in _nodos.Where(n => n.EsColumna).Select(n => n.ColumnaId))
        {
            ColumnaContexto contexto = _contexto.Leer(id);
            var tramos = new List<ConfigTramo>();

            for (int i = 0; i < contexto.Tramos.Count; i++)
            {
                tramos.Add(_configPorTramo.TryGetValue((id, i), out ConfigTramo? cfg) ? cfg : ConfigPorDefecto(_tipos));
            }

            plan.Columnas.Add(new ColumnaPlan { Id = id, Contexto = contexto, Tramos = tramos });
        }

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
