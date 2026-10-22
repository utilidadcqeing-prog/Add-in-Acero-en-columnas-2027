namespace practicacq.Vistas;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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

    /// <summary>Las barras del documento con su diámetro: la pestaña «Traslapos» necesita el diámetro de A y B.</summary>
    private readonly IReadOnlyList<TipoBarra> _barras;

    /// <summary>
    /// De dónde salen las configuraciones con nombre. La real las guarda dentro
    /// del modelo de Revit; si nadie pasa una, se usa la de memoria, así la
    /// ventana abre igual sin Revit (vista previa).
    /// </summary>
    private readonly IAlmacenConfiguraciones _almacen;

    /// <summary>
    /// Una copia de <see cref="ConfigTramo"/> por (columna, tramo). Es el
    /// germen de la persistencia que formaliza L5 ("D1 — ida y vuelta sin
    /// perder nada"): alcanza para que navegar con la lista o las flechas no
    /// pierda lo que se tecleó, y cada tramo guarda su propia copia (no una
    /// referencia compartida) incluso cuando llegó por un "aplicar al resto de...".
    /// </summary>
    private readonly Dictionary<(string ColumnaId, int Indice), ConfigTramo> _configPorTramo = new();

    /// <summary>
    /// Una copia de <see cref="ConfigLongitudinal"/> por columna (no por
    /// tramo: el acero longitudinal corre toda la altura de la columna). L4
    /// vive del lado de sus estribos menores (<see cref="ConfigLongitudinal.EstribosMenores"/>)
    /// dentro de este mismo tipo.
    /// </summary>
    private readonly Dictionary<string, ConfigLongitudinal> _longitudinalPorColumna = new();

    /// <summary>
    /// El recubrimiento elegido para cada columna, en metros -- uno solo para
    /// toda la pila (ver <see cref="ColumnaPlan.Recubrimiento"/>). Entra acá la
    /// primera vez que la columna se activa, con el del tramo que quedó activo
    /// en ese momento: "el que esté seleccionado; si son varios, el primero"
    /// (Luciano, 8-oct). Después solo lo cambia el combo.
    /// </summary>
    private readonly Dictionary<string, double> _recubrimientoPorColumna = new();

    /// <summary>Corta el eco de <c>ComboRecubrimiento_SelectionChanged</c> cuando el combo se llena por código.</summary>
    private bool _cargandoRecubrimiento;

    /// <summary>Corta los recálculos de la pestaña «Traslapos» mientras <see cref="MostrarLongitudinalEnPanel"/> llena los combos uno por uno.</summary>
    private bool _mostrandoLongitudinal;

    /// <summary>El valor normal del equipo (William, 9-sep): siempre se ofrece, lo tenga o no alguna pieza.</summary>
    private const double RecubrimientoNormal = 0.04;

    /// <summary>
    /// Una opción del combo de recubrimiento. Se muestra el VALOR y nada más:
    /// identificar de qué pieza sale obligaría a mostrar los ids de Revit, que
    /// a quien arma no le dicen nada.
    /// </summary>
    private sealed record OpcionRecubrimiento(double Metros)
    {
        public override string ToString() =>
            $"{Metros * 100:0.#} cm" + (Math.Abs(Metros - RecubrimientoNormal) < 0.0005 ? " (normal del equipo)" : "");
    }

    /// <summary>
    /// Lista maestra de todas las filas (columnas + tramos), las dos pestañas
    /// (issue #33) son vistas filtradas de esto. <see cref="List{T}"/> y no
    /// <see cref="ObservableCollection{T}"/> a propósito: nada se bindea
    /// directo a esto -- cada <c>ListBox.ItemsSource</c> recibe su propio
    /// snapshot filtrado (<c>CargarColumnas</c>), así que la notificación de
    /// cambios de ObservableCollection no aporta nada acá, solo overhead.
    /// </summary>
    private readonly List<NodoLista> _nodos = new();

    private ColumnaContexto? _columnaActiva;
    private int _tramoActivoIndice;

    /// <summary>Id representativo de la columna activa — fallback si algún tramo no trae <see cref="Tramo.IdElementoHost"/>.</summary>
    private string? _columnaIdActiva;

    /// <summary>
    /// Si el nodo activado fue la columna entera (true) o un tramo suelto
    /// (false) — decide si <see cref="NotificarElementosActivos"/> selecciona
    /// toda la pila o un único elemento. Pedido explícito: tocar un tramo
    /// selecciona solo ese tramo, tocar la columna selecciona todo.
    /// </summary>
    private bool _modoColumnaCompleta;

    /// <summary>
    /// Mientras se está activando una fila porque el usuario seleccionó algo
    /// en Revit directamente (no en la ventana), <see cref="NotificarElementosActivos"/>
    /// no debe re-emitir <see cref="ElementosActivados"/> -- si lo hiciera,
    /// <c>ComandoEstribos</c> le pediría a Revit que vuelva a seleccionar los
    /// mismos elementos, lo que dispara de nuevo el cambio de selección de
    /// Revit y entra en bucle. Ver <see cref="SincronizarDesdeRevit"/>.
    /// </summary>
    private bool _sincronizandoDesdeRevit;

    /// <summary>
    /// El espaciado no confinado se sugiere al doble del confinado, pero deja
    /// de recalcularse en cuanto el usuario lo escribe a mano: antes se pisaba
    /// en CADA pulsación de tecla del campo confinado, así que quien ajustaba
    /// el no confinado lo perdía sin aviso (el comentario del método prometía
    /// esto, pero no había ningún flag que lo implementara). Se resetea al
    /// cargar otro tramo.
    /// </summary>
    /// <summary>
    /// En alto mientras se resalta en la lista de columnetas por código (desde
    /// la planta o desde una selección hecha en Revit). Sin esto, cada elemento
    /// agregado a <c>SelectedItems</c> volvería a entrar por
    /// <see cref="ListaSeleccion_SelectionChanged"/> y activaría un tramo a
    /// mitad de camino.
    /// </summary>
    private bool _resaltandoEnLista;

    /// <summary>
    /// Los tramos ACEPTADOS: los que llevan chulito y los únicos que se
    /// generan (issue #56, reunión del 11-sep -- desapareció el paso de
    /// "aplicar" y las casillas de tildar).
    ///
    /// Es a propósito un conjunto aparte de <see cref="_configPorTramo"/>, que
    /// significa otra cosa: ahí se guarda lo que el panel tiene cargado para
    /// cada tramo, y se escribe SOLO para no perder lo tecleado al navegar
    /// (<see cref="GuardarPanelEnTramoActivo"/>). Si el chulito saliera de ahí,
    /// recorrer la columna con las flechitas dejaría todo marcado sin que nadie
    /// lo haya pedido. Acá solo se entra por un gesto explícito: "Aceptar", o un
    /// "aplicar al resto de..." que lo incluya.
    /// </summary>
    private readonly HashSet<(string ColumnaId, int Indice)> _tramosAceptados = new();

    /// <summary>
    /// Las columnas elegidas en la planta (o en la lista de columnetas). La
    /// activa siempre está adentro. Con más de una, aceptar le cae a todas
    /// -- siempre que compartan estructura, ver <see cref="MismaEstructura"/>
    /// (issue #63).
    ///
    /// Vive acá y no en los <c>SelectedItems</c> de un ListBox porque desde el
    /// issue #62 la lista de columnas principales ya no existe: la selección se
    /// hace en la planta, que no es un control con selección propia.
    /// </summary>
    private readonly HashSet<string> _columnasElegidas = new();

    private bool _noConfinadoEditadoAMano;

    /// <summary>
    /// En alto mientras el panel se llena por código (cargar un tramo, o la
    /// propia sugerencia automática). Sin esto, esas escrituras se leerían como
    /// si las hubiera tecleado el usuario.
    /// </summary>
    private bool _llenandoPanel;

    /// <summary>
    /// Se dispara con los ids de los elementos de Revit a seleccionar: todos
    /// los de la pila si se activó la columna entera, o uno solo (el
    /// <see cref="Tramo.IdElementoHost"/> del tramo) si se activó un tramo
    /// suelto -- una columna lógica puede ser varios <c>FamilyInstance</c>
    /// apilados, ver #28. Sin una sola línea de <c>Autodesk.Revit</c> acá
    /// adentro -- quien abre la ventana (ComandoEstribos) lo engancha para
    /// seleccionar los elementos reales en Revit (issue #24): selección
    /// liviana nada más, nunca abrir la familia.
    /// </summary>
    public event Action<IReadOnlyList<string>>? ElementosActivados;

    /// <summary>
    /// Issue #5 (J3): se dispara con el <see cref="OpcionesArmado"/> armado
    /// (el <see cref="PlanArmado"/> que arma <see cref="ConstruirPlanArmado"/>,
    /// más el estado del checkbox de borrado) cuando se pide generar el
    /// armado real. Sin una sola línea de Autodesk.Revit acá adentro:
    /// <c>ComandoEstribos</c> es quien lo traduce a la API real y devuelve el
    /// resultado por <see cref="MostrarResultadoGeneracion"/>.
    /// </summary>
    public event Action<OpcionesArmado>? GenerarSolicitado;

    public VentanaConfiguradorEstribos(
        IProveedorContexto contexto, IProveedorTipos tipos, bool modoSimulado = false,
        IAlmacenConfiguraciones? almacen = null)
    {
        InitializeComponent();

        _contexto = contexto;
        _tipos = tipos;
        _almacen = almacen ?? new AlmacenConfiguracionesEnMemoria();

        BannerSimulado.Visibility = modoSimulado ? Visibility.Visible : Visibility.Collapsed;

        // Los combos muestran nombres, que es lo que ConfigTramo/ConfigLongitudinal
        // guardan como texto; el diámetro que trae cada TipoBarra se usa aparte,
        // para elegir el valor por defecto (#3 y #4, issue #60).
        IReadOnlyList<TipoBarra> barras = _tipos.ListarBarras();
        List<string> nombresDeBarra = barras.Select(b => b.Nombre).ToList();

        // #64: el estribo (y cajas y grapas) solo con barras cuyo doblado
        // coincide con el cuadro de la empresa.
        ComboTipoBarra.ItemsSource = BarrasAptas(barras).Select(b => b.Nombre).ToList();
        MostrarNotaBarrasEstribo(barras);
        ComboBarraCaraX.ItemsSource = nombresDeBarra;
        ComboBarraCaraY.ItemsSource = nombresDeBarra;
        ComboBarraEsquinas.ItemsSource = nombresDeBarra;
        _barras = barras;

        // Cada combo de gancho ve SOLO los de su estilo: en Revit un gancho
        // solo se puede aplicar a barras de su mismo estilo, así que ofrecer
        // los dos mezclados es ofrecer ganchos que fallan recién al generar.
        // Los dos combos están ocultos (el valor se fija por defecto, reunión
        // del 10-sep) pero se llenan igual, porque de ahí los lee el panel.
        IReadOnlyList<TipoGancho> ganchos = _tipos.ListarGanchos();
        ComboTipoGancho.ItemsSource = ganchos.Where(g => g.Estilo == EstiloGancho.Estribo).Select(g => g.Nombre).ToList();
        ComboGanchoLongitudinal.ItemsSource = ganchos.Where(g => g.Estilo == EstiloGancho.Estandar).Select(g => g.Nombre).ToList();

        AvisarSiFaltaAlgoPorDefecto(barras, ganchos);

        // Pares del 2 al 20 -- "nada se teclea a mano" aplica también acá,
        // no solo a los tipos de barra.
        List<int> paresHasta20 = Enumerable.Range(1, 10).Select(n => n * 2).ToList();
        ComboBarrasX.ItemsSource = paresHasta20;
        ComboBarrasY.ItemsSource = paresHasta20;
        ComboBarrasX.SelectedItem = 2;
        ComboBarrasY.SelectedItem = 2;

        // Fase 5 (#7): la barra comercial viene en 6 o 12 m -- no es un rango
        // libre, son las dos longitudes de stock. El tramo final de una
        // corrida puede quedar más corto (se recorta), pero eso no lo elige
        // el usuario acá.
        ComboLongitudBarra.ItemsSource = new[] { 6.0, 12.0 };
        ComboLongitudBarra.SelectedItem = 12.0;

        ComboRedondeo.ItemsSource = PlanTraslapos.Redondeos;
        ComboRedondeo.SelectedItem = PlanTraslapos.RedondeoPorDefecto;

        ComboSolape.ItemsSource = new[] { TextoSolapeCadaDiametro, TextoSolapeMayor };
        ComboSolape.SelectedItem = TextoSolapeCadaDiametro;

        RadioUniforme.Checked += (_, _) => { ActualizarVisibilidadModo(); RevalidarPanel(); };
        RadioConfinado.Checked += (_, _) => { ActualizarVisibilidadModo(); RevalidarPanel(); };

        // Cableado acá y no en el XAML: si estos se disparan durante el
        // parseo inicial (InitializeComponent, al asignar los Text="..." del
        // propio XAML), los controles que tocan todavía no existen como
        // campos conectados y esto revienta con NullReferenceException --
        // pasó de verdad con CampoEspaciadoConfinado_TextChanged, ver commit.
        CampoEspaciadoConfinado.TextChanged += CampoEspaciadoConfinado_TextChanged;
        CampoEspaciadoConfinado.TextChanged += (_, _) => RevalidarPanel();

        PanelPlanta.ColumnaClicked += ElegirColumnaDesdePlanta;
        PanelPlanta.ColumnasEnRectangulo += ElegirColumnasDesdeRectangulo;
        CampoEspaciadoUniforme.TextChanged += (_, _) => RevalidarPanel();
        CampoL1.TextChanged += (_, _) => RevalidarPanel();
        CampoEspaciadoNoConfinado.TextChanged += CampoEspaciadoNoConfinado_TextChanged;
        CampoEspaciadoNoConfinado.TextChanged += (_, _) => RevalidarPanel();
        ComboTipoBarra.SelectionChanged += (_, _) => RevalidarPanel();
        ComboTipoGancho.SelectionChanged += (_, _) => RevalidarPanel();

        // Cualquier cambio que mueva los traslapos recalcula la pestaña
        // «Traslapos». Cableado acá y no en el XAML por lo mismo que los de
        // arriba. El recubrimiento no participa: no cambia las alturas. La
        // cantidad de barras sí: decide qué números caen en A y cuáles en B.
        foreach (ComboBox combo in new[]
                 {
                     ComboBarraCaraX, ComboBarraCaraY, ComboBarraEsquinas, ComboBarrasX, ComboBarrasY,
                     ComboLongitudBarra, ComboRedondeo, ComboSolape,
                 })
            combo.SelectionChanged += (_, _) => { if (!_mostrandoLongitudinal) RecalcularTraslapos(); };

        // Pestaña «Vista previa» (VentanaConfiguradorEstribos.VistaPrevia.cs).
        ConectarVistaPrevia();

        RefrescarConfiguracionesGuardadas();
        CargarColumnas();
        ActualizarResumenDeGeneracion();
    }

    // -------------------------------------------------------- lista / árbol

    private void CargarColumnas()
    {
        _nodos.Clear();

        foreach (ColumnaResumen resumen in _contexto.ListarColumnas())
            AgregarColumnaYTramos(resumen);

        // Issue #62: la lista de columnas principales se fue (se eligen en la
        // planta). Queda la de columnetas, que son delgadas y suelen estar
        // juntas -- pincharlas en la planta es incómodo. Se oculta si el
        // modelo no tiene ninguna, para no dejar una pestaña vacía.
        List<NodoLista> columnetas = _nodos.Where(n => n.EsColumneta).ToList();
        ListaColumnetas.ItemsSource = columnetas;
        PestanaColumnetas.Visibility = columnetas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        NodoLista? primerTramo = _nodos.FirstOrDefault(n => !n.EsColumna);
        if (primerTramo is not null) Activar(primerTramo);
    }

    private void AgregarColumnaYTramos(ColumnaResumen resumen)
    {
        ColumnaContexto columna = _contexto.Leer(resumen.Id);

        _nodos.Add(new NodoLista
        {
            EsColumna = true,
            EsColumneta = resumen.EsColumneta,
            ColumnaId = resumen.Id,
            Etiqueta = $"{resumen.Marca}  ({columna.Tramos.Count} tramos)",
        });

        foreach (Tramo tramo in columna.Tramos)
        {
            _nodos.Add(new NodoLista
            {
                EsColumna = false,
                EsColumneta = resumen.EsColumneta,
                ColumnaId = resumen.Id,
                TramoIndice = tramo.Indice,
                Etiqueta = $"Tramo {tramo.Indice}  ·  {tramo.Altura:0.00} m  ·  {Rotulo(tramo.Tipo)}",
                IdElementoHost = tramo.IdElementoHost,
            });
        }
    }

    private static string Rotulo(TipoTramo tipo) => tipo switch
    {
        TipoTramo.Libre => "libre",
        TipoTramo.DentroDeViga => "nudo",
        TipoTramo.ContraZapata => "contra zapata",
        _ => tipo.ToString(),
    };

    /// <summary>
    /// Selección en la lista de columnetas (la única que quedó, issue #62).
    /// Resaltar es ELEGIR columnas: con una sola se activa esa; con varias no
    /// se cambia el tramo activo (si no, la vista de Revit se movería una vez
    /// por Ctrl+clic).
    /// </summary>
    private void ListaSeleccion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Si esto lo disparó una selección hecha por código, quien la hizo ya
        // llama a Activar explícitamente después -- sin este corte se llama dos
        // veces reentrante, pisando _columnaActiva/_tramoActivoIndice a mitad de
        // camino. Eso dejaba el estado inconsistente y probablemente era la
        // causa real del crash de Revit al cerrar la ventana (una excepción ahí
        // se escapa por el lado nativo del evento de selección de Revit).
        if (_sincronizandoDesdeRevit || _resaltandoEnLista) return;
        if (sender is not ListBox lista) return;

        List<NodoLista> seleccionados = lista.SelectedItems.Cast<NodoLista>().ToList();
        if (seleccionados.Count == 0) return;

        _columnasElegidas.Clear();
        foreach (NodoLista nodo in seleccionados) _columnasElegidas.Add(nodo.ColumnaId);

        if (seleccionados.Count == 1) Activar(seleccionados[0]);
        else RedibujarElevacion();

        RedibujarPanelPlanta();
        AvisarSiLaSeleccionNoEsUniforme();
    }

    // ------------------------------------------------------- tramo activo

    private void Activar(NodoLista nodo)
    {
        GuardarPanelEnTramoActivo();
        GuardarLongitudinalDeColumnaActiva();

        _columnaActiva = _contexto.Leer(nodo.ColumnaId);
        _columnaIdActiva = nodo.ColumnaId;

        // La activa siempre forma parte de lo elegido: aceptar le cae a ella
        // aunque nadie la haya resaltado aparte.
        _columnasElegidas.Add(nodo.ColumnaId);
        _tramoActivoIndice = nodo.EsColumna ? 0 : nodo.TramoIndice;
        _modoColumnaCompleta = nodo.EsColumna;

        RedibujarElevacion();
        CargarPanelDelTramoActivo();
        CargarLongitudinalDeColumnaActiva();
        CargarRecubrimientoDeColumnaActiva();
        MostrarAvisos();
        NotificarElementosActivos();

        // Resaltar en la planta la misma columna que acaba de activarse -- por
        // cualquier camino: clic en la lista, clic en el propio panel, flechas
        // de la elevación, o sincronización desde una selección hecha en Revit.
        // Si su pestaña no está a la vista el panel mide cero y no dibuja nada,
        // así que no hace falta preguntarlo.
        RedibujarPanelPlanta();
    }

    /// <summary>
    /// La planta pasó de ser un MODO que tapaba la lista (había un botón para
    /// alternar) a ser la primera pestaña, siempre a mano -- reunión del
    /// 10-sep: en lista se perdía de vista cuál columna era cuál, y en planta
    /// no se podía editar un tramo puntual. Ahora conviven, y los tramos
    /// tienen su propio panel al lado.
    /// </summary>
    private void PestanasSeleccion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, PestanasSeleccion)) return;
        RedibujarPanelPlanta();
    }

    // ------------------------------------------ configuraciones guardadas

    /// <summary>
    /// En alto mientras el combo se rellena por código: cambiar su
    /// <c>ItemsSource</c> dispara <c>SelectionChanged</c>, y eso cargaría una
    /// configuración que el usuario no eligió.
    /// </summary>
    private bool _refrescandoConfiguraciones;

    private void RefrescarConfiguracionesGuardadas(string? seleccionar = null)
    {
        _refrescandoConfiguraciones = true;
        try
        {
            List<string> nombres = _almacen.Listar().Select(g => g.Nombre).OrderBy(n => n).ToList();
            ComboConfiguraciones.ItemsSource = nombres;
            ComboConfiguraciones.SelectedItem = seleccionar is not null && nombres.Contains(seleccionar) ? seleccionar : null;
        }
        finally
        {
            _refrescandoConfiguraciones = false;
        }
    }

    /// <summary>
    /// Elegir una configuración solo CARGA el panel -- no acepta ningún tramo.
    /// Llevarla a los tramos es el otro gesto (Aceptar), a propósito: así se
    /// puede mirar qué trae antes de decidir nada.
    /// </summary>
    private void ComboConfiguraciones_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refrescandoConfiguraciones) return;
        if (ComboConfiguraciones.SelectedItem is not string nombre) return;
        if (_almacen.Listar().FirstOrDefault(g => g.Nombre == nombre) is not { } guardada) return;

        MostrarConfigEnPanel(guardada.Tramo);

        // Los estribos menores (grapas y cajas) no están en el panel: se editan
        // aparte y viven por columna. Cargar la configuración los lleva a la
        // columna activa, si no el resumen que se muestra sería mentira.
        ConfigLongitudinal longitudinal = guardada.Longitudinal.Copiar();
        bool eraVieja = longitudinal.MigrarBarrasPorCara();
        longitudinal.MigrarEstribosMenores();
        int descartadas = longitudinal.DescartarMenoresFueraDeRango();
        if (_columnaIdActiva is string idColumna) _longitudinalPorColumna[idColumna] = longitudinal;
        MostrarLongitudinalEnPanel(longitudinal);

        CampoNombreConfiguracion.Text = nombre;
        RevalidarPanel();

        string menores = ResumenEstribosMenores(longitudinal);
        TextoEstado.Text = $"Configuración «{nombre}» cargada en el panel ({menores.ToLowerInvariant().TrimEnd('.')}). " +
                           "Todavía no se aplicó a ningún tramo: para eso, aprieta Aceptar.";
        if (eraVieja)
            TextoEstado.Text += " Era una configuración con barras por tipo A/B: las esquinas tomaron la barra A y las caras la B; revísalas.";
        if (descartadas > 0)
            TextoEstado.Text += $" Se quitaron {descartadas} estribo(s) menor(es) que apuntaban a barras que esta configuración ya no tiene.";
    }

    private void BotonGuardarConfiguracion_Click(object sender, RoutedEventArgs e)
    {
        string nombre = CampoNombreConfiguracion.Text.Trim();
        if (nombre.Length == 0)
        {
            TextoEstado.Text = "Escribe un nombre para la configuración antes de guardarla.";
            return;
        }

        bool reemplaza = _almacen.Listar().Any(g => g.Nombre == nombre);

        // Copia: si la configuración compartiera las listas de grapas/cajas con
        // la columna de la que salió, seguir editando los estribos menores de
        // esa columna cambiaría también lo guardado.
        _almacen.Guardar(new ConfiguracionGuardada
        {
            Nombre = nombre,
            Tramo = LeerConfigDelPanel(),
            Longitudinal = LeerLongitudinalConEstribosMenores().Copiar(),
        });

        RefrescarConfiguracionesGuardadas(nombre);
        TextoEstado.Text = reemplaza
            ? $"Configuración «{nombre}» actualizada."
            : $"Configuración «{nombre}» guardada.";
    }

    private void BotonBorrarConfiguracion_Click(object sender, RoutedEventArgs e)
    {
        if (ComboConfiguraciones.SelectedItem is not string nombre)
        {
            TextoEstado.Text = "Elige en la lista la configuración que quieras borrar.";
            return;
        }

        _almacen.Borrar(nombre);
        CampoNombreConfiguracion.Text = "";
        RefrescarConfiguracionesGuardadas();
        TextoEstado.Text = $"Configuración «{nombre}» borrada.";
    }

    /// <summary>
    /// La planta muestra las dos cosas por separado, que desde el issue #56 ya
    /// no son la misma: qué columnas están ELEGIDAS (a las que les cae Aceptar)
    /// y cuáles tienen al menos un tramo ACEPTADO (las que se van a generar).
    /// </summary>
    private void RedibujarPanelPlanta()
    {
        HashSet<string> configuradas = _tramosAceptados.Select(t => t.ColumnaId).ToHashSet();

        PanelPlanta.Cargar(
            _contexto.ListarColumnas(), _contexto.ListarRejillas(),
            _columnaIdActiva, _columnasElegidas, configuradas);
    }

    /// <summary>
    /// Clic en una columna del selector visual de planta: la elige y la activa.
    /// Ya no tilda nada -- el tilde desapareció con el issue #56: lo que se
    /// genera se decide aceptando cada tramo en el panel de la derecha. Con
    /// Ctrl se suma (o se saca) de lo ya elegido en vez de reemplazarlo.
    /// </summary>
    private void ElegirColumnaDesdePlanta(string columnaId, bool sumar)
    {
        NodoLista? nodo = _nodos.FirstOrDefault(n => n.EsColumna && n.ColumnaId == columnaId);
        if (nodo is null) return;

        if (!sumar)
        {
            _columnasElegidas.Clear();
        }
        else if (_columnasElegidas.Contains(columnaId) && _columnasElegidas.Count > 1)
        {
            // Ctrl+clic sobre una ya elegida la saca -- salvo que sea la única,
            // porque quedarse sin ninguna no es un estado útil.
            _columnasElegidas.Remove(columnaId);
            SincronizarResaltadoDeColumnetas();
            RedibujarPanelPlanta();
            AvisarSiLaSeleccionNoEsUniforme();
            return;
        }

        _columnasElegidas.Add(columnaId);
        SincronizarResaltadoDeColumnetas();
        Activar(nodo);
        AvisarSiLaSeleccionNoEsUniforme();
    }

    /// <summary>
    /// Arrastre sobre la planta (reunión 10-sep): elige todas las columnas
    /// alcanzadas. A diferencia del clic suelto no alterna -- las deja
    /// elegidas, que es lo que uno espera de una selección por rectángulo.
    /// </summary>
    private void ElegirColumnasDesdeRectangulo(IReadOnlyList<string> idsColumnas, bool sumar)
    {
        var alcanzadas = new HashSet<string>(idsColumnas);
        List<NodoLista> columnas = _nodos.Where(n => n.EsColumna && alcanzadas.Contains(n.ColumnaId)).ToList();
        if (columnas.Count == 0) return;

        if (!sumar) _columnasElegidas.Clear();
        foreach (NodoLista nodo in columnas) _columnasElegidas.Add(nodo.ColumnaId);

        SincronizarResaltadoDeColumnetas();
        Activar(columnas[0]);
        AvisarSiLaSeleccionNoEsUniforme();
    }

    /// <summary>
    /// Refleja <see cref="_columnasElegidas"/> en la lista de columnetas, que es
    /// el único ListBox que quedó (issue #62) y muestra solo una parte de las
    /// columnas: las que no están ahí simplemente no se reflejan, se ven
    /// elegidas en la planta.
    /// </summary>
    private void SincronizarResaltadoDeColumnetas()
    {
        _resaltandoEnLista = true;
        try
        {
            ListaColumnetas.SelectedItems.Clear();
            foreach (NodoLista nodo in ListaColumnetas.Items.OfType<NodoLista>()
                         .Where(n => n.EsColumna && _columnasElegidas.Contains(n.ColumnaId)))
            {
                ListaColumnetas.SelectedItems.Add(nodo);
            }
        }
        finally
        {
            _resaltandoEnLista = false;
        }
    }

    /// <summary>
    /// Issue #63: configurar varias columnas de una sola vez solo tiene sentido
    /// si comparten estructura -- "el mismo nudo, los mismos tramos… solo que
    /// una va a tener el traslapo a otra altura porque sea más larga o más
    /// corta" (William, 11-sep). La diferencia de ALTURA no importa; la de
    /// cantidad y tipo de tramos sí, porque lo que se copia es tramo por tramo.
    /// </summary>
    private static bool MismaEstructura(ColumnaContexto a, ColumnaContexto b) =>
        a.Tramos.Count == b.Tramos.Count &&
        a.Tramos.Select(t => t.Tipo).SequenceEqual(b.Tramos.Select(t => t.Tipo));

    /// <summary>
    /// Las columnas elegidas que comparten estructura con la activa, y las que
    /// no. La activa siempre encabeza la lista de compatibles.
    /// </summary>
    private (List<(string Id, ColumnaContexto Ctx)> Compatibles, List<string> Descartadas) ColumnasCompatibles()
    {
        var compatibles = new List<(string, ColumnaContexto)>();
        var descartadas = new List<string>();

        if (_columnaActiva is null || _columnaIdActiva is not string idActiva)
            return (compatibles, descartadas);

        compatibles.Add((idActiva, _columnaActiva));

        foreach (string id in _columnasElegidas.Where(i => i != idActiva).OrderBy(i => i))
        {
            ColumnaContexto otra = _contexto.Leer(id);

            if (MismaEstructura(_columnaActiva, otra)) compatibles.Add((id, otra));
            else descartadas.Add($"{otra.Marca} ({CuantosTramos(otra.Tramos.Count)})");
        }

        return (compatibles, descartadas);
    }

    private static string CuantosTramos(int cuantos) => cuantos == 1 ? "1 tramo" : $"{cuantos} tramos";

    /// <summary>
    /// Avisa apenas se eligen varias columnas que no comparten estructura, sin
    /// esperar a que alguien apriete Aceptar: el issue #63 pide justamente eso,
    /// avisar en vez de aplicar en silencio.
    /// </summary>
    private void AvisarSiLaSeleccionNoEsUniforme()
    {
        if (_columnasElegidas.Count <= 1) return;

        (List<(string Id, ColumnaContexto Ctx)> compatibles, List<string> descartadas) = ColumnasCompatibles();
        if (_columnaActiva is null) return;

        bool esUna = descartadas.Count == 1;

        TextoEstado.Text = descartadas.Count == 0
            ? $"{compatibles.Count} columnas elegidas, todas con la misma estructura: lo que aceptes les cae a todas."
            : $"{_columnasElegidas.Count} columnas elegidas, pero {string.Join(", ", descartadas)} " +
              $"{(esUna ? "no tiene" : "no tienen")} la misma estructura que " +
              $"{_columnaActiva.Marca} ({CuantosTramos(_columnaActiva.Tramos.Count)}): " +
              $"{(esUna ? "queda" : "quedan")} afuera de lo que aceptes.";
    }

    /// <summary>
    /// Issue #24 (segundo sentido, reunión 9-sep): si el usuario selecciona
    /// una columna directamente en Revit (no en esta ventana), activa la fila
    /// correspondiente acá -- mismo camino que un clic en la lista
    /// (<see cref="Activar"/>), sin volver a mandarle la selección a Revit
    /// (ya la tiene, es de ahí que salió). <c>ComandoEstribos</c> llama esto
    /// desde un listener del cambio de selección nativo de Revit.
    ///
    /// Busca primero por tramo (<see cref="NodoLista.IdElementoHost"/>, más
    /// específico) y si no encuentra nada cae a buscar por columna completa
    /// (<see cref="NodoLista.ColumnaId"/>) -- cubre el caso de seleccionar el
    /// elemento de más abajo de la pila, que es el mismo id que la fila de
    /// columna usa como <c>ColumnaId</c>. Si la selección de Revit no
    /// corresponde a ninguna columna conocida (otro tipo de elemento, o
    /// varios elementos sueltos), no hace nada -- no hay fila que activar.
    /// </summary>
    public void SincronizarDesdeRevit(IReadOnlyList<string> idsSeleccionados)
    {
        if (idsSeleccionados.Count == 0) return;

        var idsSet = new HashSet<string>(idsSeleccionados);

        // TODOS los que coincidan, no solo el primero: seleccionar cinco
        // columnas en Revit y que acá se active una sola, arbitraria (la
        // primera en el orden del modelo, ni siquiera en el orden de
        // selección), era lo que se veía hasta la reunión del 10-sep.
        List<NodoLista> coinciden = _nodos
            .Where(n => !n.EsColumna && n.IdElementoHost is not null && idsSet.Contains(n.IdElementoHost))
            .ToList();

        // Si no cae en ningún tramo puede ser el elemento de más abajo de la
        // pila, que es el mismo id que la fila de columna usa como ColumnaId.
        if (coinciden.Count == 0)
            coinciden = _nodos.Where(n => n.EsColumna && idsSet.Contains(n.ColumnaId)).ToList();

        if (coinciden.Count == 0) return;

        _sincronizandoDesdeRevit = true;
        try
        {
            NodoLista primero = coinciden[0];
            if (primero.EsColumneta) PestanaColumnetas.IsSelected = true;

            _columnasElegidas.Clear();
            foreach (NodoLista nodo in coinciden) _columnasElegidas.Add(nodo.ColumnaId);

            SincronizarResaltadoDeColumnetas();
            Activar(primero);
        }
        finally
        {
            _sincronizandoDesdeRevit = false;
        }
    }

    private void IrA(int indice)
    {
        if (_columnaActiva is null) return;

        GuardarPanelEnTramoActivo();
        _tramoActivoIndice = Math.Clamp(indice, 0, _columnaActiva.Tramos.Count - 1);

        // Moverse con las flechas siempre para en un tramo puntual, nunca en
        // "la columna entera" -- eso solo se activa desde la fila en negrita.
        _modoColumnaCompleta = false;

        RedibujarElevacion();
        CargarPanelDelTramoActivo();
        NotificarElementosActivos();
    }

    /// <summary>
    /// Selecciona en Revit toda la pila si está activa la columna entera, o
    /// solo el elemento del tramo puntual si no -- pedido explícito: tocar un
    /// tramo selecciona ese tramo, tocar la columna selecciona todo (#24).
    /// </summary>
    private void NotificarElementosActivos()
    {
        if (_columnaActiva is null) return;

        List<string> ids;

        if (_modoColumnaCompleta)
        {
            ids = _columnaActiva.Tramos
                .Select(t => t.IdElementoHost)
                .Where(id => id is not null)
                .Distinct()
                .ToList()!;
        }
        else
        {
            string? idTramo = _columnaActiva.Tramos.ElementAtOrDefault(_tramoActivoIndice)?.IdElementoHost;
            ids = idTramo is not null ? new List<string> { idTramo } : new List<string>();
        }

        // Sin IdElementoHost (proveedor falso, o algo no se pudo resolver):
        // cae al id representativo de la columna en vez de no seleccionar nada.
        if (ids.Count == 0 && _columnaIdActiva is not null) ids.Add(_columnaIdActiva);

        // Ver _sincronizandoDesdeRevit: si esta activación vino de una
        // selección hecha en Revit, no hay que devolverle la selección --
        // ya la tiene, es de ahí que salió esta activación.
        if (ids.Count > 0 && !_sincronizandoDesdeRevit) ElementosActivados?.Invoke(ids);
    }

    private void BotonAnterior_Click(object sender, RoutedEventArgs e) => IrA(_tramoActivoIndice - 1);

    private void BotonSiguiente_Click(object sender, RoutedEventArgs e) => IrA(_tramoActivoIndice + 1);

    /// <summary>
    /// Los avisos que <c>LectorContexto</c> dejó al leer esta columna (zapata,
    /// sección que cambia, ninguna viga tocándola...) — antes se calculaban y
    /// se perdían: no había dónde verlos. Vacío casi siempre, por eso el panel
    /// se oculta entero cuando no hay nada que decir.
    /// </summary>
    private void MostrarAvisos()
    {
        IReadOnlyList<string> avisos = _columnaActiva?.Avisos ?? Array.Empty<string>();

        PanelAvisos.Visibility = avisos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TextoAvisos.Text = string.Join("\n", avisos.Select(a => "• " + a));
    }

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
            errores.Add("Elige un tipo de barra.");
        if (string.IsNullOrWhiteSpace(cfg.TipoGanchoNombre))
            errores.Add("Elige un tipo de gancho.");

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

            // El caso "esto es un nudo" ya no se valida acá: desde el issue
            // #58 el modo Confinado queda directamente DESHABILITADO con un
            // nudo activo (ver CargarPanelDelTramoActivo), así que no hay forma
            // de llegar hasta acá con esa combinación.
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
    /// Autocompleta el espaciado no confinado al doble del confinado -- pero
    /// solo mientras el usuario no lo haya escrito a mano
    /// (<see cref="_noConfinadoEditadoAMano"/>).
    /// </summary>
    private void CampoEspaciadoConfinado_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_llenandoPanel || _noConfinadoEditadoAMano) return;

        double confinado = LeerDouble(CampoEspaciadoConfinado.Text);

        _llenandoPanel = true;
        try { CampoEspaciadoNoConfinado.Text = (confinado * 2).ToString("0.###", CultureInfo.InvariantCulture); }
        finally { _llenandoPanel = false; }
    }

    /// <summary>
    /// Si el texto lo escribió el usuario (y no la sugerencia automática ni la
    /// carga de un tramo), a partir de ahí el campo es suyo y no se vuelve a
    /// pisar.
    /// </summary>
    private void CampoEspaciadoNoConfinado_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_llenandoPanel) return;
        _noConfinadoEditadoAMano = true;
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
        // Cargar otro tramo empieza de cero: lo que se muestra es lo guardado
        // de ESE tramo, no una edición manual del anterior.
        _noConfinadoEditadoAMano = false;
        _llenandoPanel = true;
        try
        {
            RadioUniforme.IsChecked = cfg.Modo == ModoEstribo.Uniforme;
            RadioConfinado.IsChecked = cfg.Modo == ModoEstribo.Confinado;
            ActualizarVisibilidadModo();

            // Una configuración guardada con una barra que no es apta para
            // estribo (#64) pasa a la barra por defecto, avisando.
            if (cfg.TipoBarraNombre is string nombreBarra && !BarrasAptas(_barras).Any(b => b.Nombre == nombreBarra))
            {
                cfg.TipoBarraNombre = BarraPorDefecto(_tipos, NumeroVarillaEstribo);
                TextoEstado.Text = $"«{nombreBarra}» no es apta para estribo según el cuadro; se cambió por «{cfg.TipoBarraNombre}».";
            }

            ComboTipoBarra.SelectedItem = cfg.TipoBarraNombre;
            ComboTipoGancho.SelectedItem = cfg.TipoGanchoNombre;

            CampoEspaciadoUniforme.Text = Formato(cfg.EspaciadoUniforme);
            CampoL1.Text = Formato(cfg.LongitudConfinamiento);
            CampoEspaciadoConfinado.Text = Formato(cfg.EspaciadoConfinado);
            CampoEspaciadoNoConfinado.Text = Formato(cfg.EspaciadoNoConfinado);
        }
        finally
        {
            _llenandoPanel = false;
        }
    }

    /// <summary>
    /// El add-in no crea tipos de gancho: si el documento no tiene uno del
    /// estilo y ángulo esperados, se usa el que haya de ese estilo, y eso hay
    /// que decirlo -- si no, el armado sale con un gancho que nadie eligió.
    /// </summary>
    private void AvisarSiFaltaAlgoPorDefecto(IReadOnlyList<TipoBarra> barras, IReadOnlyList<TipoGancho> ganchos)
    {
        var faltan = new List<string>();

        // Issue #60: la barra por defecto se elige por DIÁMETRO contra el cuadro
        // de la empresa, no por nombre ("#3", "#3 COL", "3"… cambian de modelo a
        // modelo) ni por "la primera de la lista", que en el modelo de William
        // daba la #10. Si el documento no tiene ese diámetro se cae a la primera
        // que haya, pero avisando.
        foreach ((int numero, string para) in new[] { (NumeroVarillaEstribo, "el estribo"), (NumeroVarillaLongitudinal, "la longitudinal") })
        {
            IReadOnlyList<TipoBarra> candidatas = numero == NumeroVarillaEstribo ? BarrasAptas(barras) : barras;
            if (TipoBarra.PorDefecto(candidatas, numero) is null)
                faltan.Add(candidatas.Count == 0
                    ? $"no hay ninguna barra apta para {para}"
                    : $"no hay ninguna barra #{numero} para {para}; se usa «{candidatas[0].Nombre}»");
        }

        if (TipoGancho.PorDefecto(ganchos, EstiloGancho.Estribo, 135) is not { } estribo)
            faltan.Add("no hay ningún gancho de estilo estribo/tirante");
        else if (Math.Abs(estribo.AnguloGrados - 135) >= 1)
            faltan.Add($"no hay gancho de estribo de 135°; se usa «{estribo.Nombre}» ({estribo.AnguloGrados:0}°)");

        if (TipoGancho.PorDefecto(ganchos, EstiloGancho.Estandar, 90) is not { } zapata)
            faltan.Add("no hay ningún gancho de estilo estándar para el anclaje en la zapata");
        else if (Math.Abs(zapata.AnguloGrados - 90) >= 1)
            faltan.Add($"no hay gancho estándar de 90° para la zapata; se usa «{zapata.Nombre}» ({zapata.AnguloGrados:0}°)");

        if (faltan.Count > 0)
            TextoEstado.Text = "Atención: " + string.Join("; ", faltan) + ".";
    }

    /// <summary>Barra por defecto del estribo: #3 (William, 11-sep).</summary>
    private const int NumeroVarillaEstribo = 3;

    /// <summary>Barra por defecto de la longitudinal, esquinas y caras: #4 ("todo número cuatro por defecto").</summary>
    private const int NumeroVarillaLongitudinal = 4;

    /// <summary>Opciones del combo «Solape mínimo»: <see cref="ConfigLongitudinal.SolapeMayorDeLosDos"/> false / true.</summary>
    private const string TextoSolapeCadaDiametro = "El de cada diámetro";
    private const string TextoSolapeMayor = "El mayor de la columna";

    /// <summary>
    /// El tipo de barra de esa varilla, o el primero de la lista si el
    /// documento no la tiene -- que ese caso se avisa una vez al abrir, en
    /// <see cref="AvisarSiFaltaAlgoPorDefecto"/>.
    /// </summary>
    private static string? BarraPorDefecto(IProveedorTipos tipos, int numeroVarilla)
    {
        IReadOnlyList<TipoBarra> barras = numeroVarilla == NumeroVarillaEstribo
            ? BarrasAptas(tipos.ListarBarras())
            : tipos.ListarBarras();

        return TipoBarra.PorDefecto(barras, numeroVarilla)?.Nombre ?? barras.FirstOrDefault()?.Nombre;
    }

    private static ConfigTramo ConfigPorDefecto(IProveedorTipos tipos) => new()
    {
        // Issue #60: por diámetro contra el cuadro de la empresa. Antes era "el
        // primero alfabéticamente", que no es un criterio -- en el modelo de
        // William daba la #10.
        TipoBarraNombre = BarraPorDefecto(tipos, NumeroVarillaEstribo),
        // Estribo/tirante de 135°, confirmado por William el 10-sep.
        TipoGanchoNombre = TipoGancho.PorDefecto(tipos.ListarGanchos(), EstiloGancho.Estribo, 135)?.Nombre,
        EspaciadoUniforme = 0.20,
        LongitudConfinamiento = 0.50,
        EspaciadoConfinado = 0.07,
        EspaciadoNoConfinado = 0.14,
    };

    private void GuardarPanelEnTramoActivo()
    {
        if (_columnaActiva is null || _columnaIdActiva is not string idColumna) return;

        var clave = (idColumna, _tramoActivoIndice);
        _configPorTramo[clave] = LeerConfigDelPanel();
    }

    private void CargarPanelDelTramoActivo()
    {
        if (_columnaActiva is null || _columnaIdActiva is not string idColumna) return;

        var clave = (idColumna, _tramoActivoIndice);
        ConfigTramo cfg = _configPorTramo.TryGetValue(clave, out ConfigTramo? existente)
            ? existente
            : ConfigPorDefecto(_tipos);

        MostrarConfigEnPanel(cfg);

        Tramo tramo = _columnaActiva.Tramos[_tramoActivoIndice];
        TextoTramoActivo.Text = $"{_columnaActiva.Marca} · {_tramoActivoIndice + 1}/{_columnaActiva.Tramos.Count} · {Rotulo(tramo.Tipo)}";

        // Issue #58: en el nudo el estribo va siempre uniforme -- "obliga que en
        // los nudos sea uniforme, no se permita meter confinamiento en el nudo"
        // (William, 11-sep). Se deshabilita después de MostrarConfigEnPanel, que
        // es quien pone los radios según lo guardado: un tramo que quedó en
        // confinado de antes (o una configuración guardada que viene en
        // confinado) se corrige acá en vez de quedar en un estado imposible.
        // Contra la zapata tampoco hay confinamiento (Luciano, 20-oct): ahí el
        // generador ni siquiera pone estribos.
        bool esNudo = tramo.Tipo is TipoTramo.DentroDeViga or TipoTramo.ContraZapata;

        RadioConfinado.IsEnabled = !esNudo;
        TextoModoFijo.Text = tramo.Tipo == TipoTramo.ContraZapata
            ? "El tramo contra zapata no lleva estribos: al generar se salta (el acero longitudinal sí pasa)."
            : "En los nudos el estribo va siempre uniforme: no se permite confinamiento dentro del canto de la viga.";
        TextoModoFijo.Visibility = esNudo ? Visibility.Visible : Visibility.Collapsed;

        if (esNudo && RadioConfinado.IsChecked == true)
        {
            _llenandoPanel = true;
            try { RadioUniforme.IsChecked = true; }
            finally { _llenandoPanel = false; }

            ActualizarVisibilidadModo();
        }

        ActualizarBotonesDeAceptar(tramo);
        RevalidarPanel();
    }

    /// <summary>
    /// Los botones de aceptar, armados contra el tramo activo (issue #57):
    /// "aplicar al resto de los tramos libres" solo aparece con un tramo libre
    /// activo, "al resto de los nudos" solo con un nudo. Contextual, no un botón
    /// genérico -- "seleccionas un tramo libre, lo configuras […] y está la
    /// opción 'aplicar al resto de tramos libres' y pan, se coloca" (William).
    ///
    /// Los conteos son del RESTO (sin el activo, que se acepta igual) y salen
    /// del mismo helper que después se usa para aplicar
    /// (<see cref="TramosDeLaColumnaActiva"/>), así que no pueden discrepar.
    /// Con 0 el botón no se muestra: una columna de un solo tramo libre no
    /// ofrece "aplicar al resto" como si hiciera algo.
    /// </summary>
    private void ActualizarBotonesDeAceptar(Tramo activo)
    {
        int delMismoTipo = TramosDeLaColumnaActiva(t => t.Tipo == activo.Tipo).Count - 1;
        int enLaColumna = TramosDeLaColumnaActiva(null).Count - 1;

        BotonAceptarMismoTipo.Content = $"✓  Aceptar y aplicar al resto de {RotuloPlural(activo.Tipo)} ({delMismoTipo})";
        BotonAceptarMismoTipo.Visibility = delMismoTipo > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Si todos los tramos de la columna son del mismo tipo, los dos botones
        // harían exactamente lo mismo: se muestra uno solo.
        BotonAceptarColumna.Content = $"✓  Aceptar y aplicar a toda la columna ({enLaColumna + 1})";
        BotonAceptarColumna.Visibility = enLaColumna > 0 && delMismoTipo != enLaColumna
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string RotuloPlural(TipoTramo tipo) => tipo switch
    {
        TipoTramo.Libre => "los tramos libres",
        TipoTramo.DentroDeViga => "los nudos",
        TipoTramo.ContraZapata => "los tramos contra zapata",
        _ => "los tramos de ese tipo",
    };

    // -------------------------------------------------- acero longitudinal (L4)

    private ConfigLongitudinal LeerLongitudinalDelPanel() => new()
    {
        BarrasPorCaraX = ComboBarrasX.SelectedItem as int? ?? 2,
        BarrasPorCaraY = ComboBarrasY.SelectedItem as int? ?? 2,
        TipoBarraCaraXNombre = ComboBarraCaraX.SelectedItem as string,
        TipoBarraCaraYNombre = ComboBarraCaraY.SelectedItem as string,
        TipoBarraEsquinasNombre = ComboBarraEsquinas.SelectedItem as string,
        LongitudComercialBarra = ComboLongitudBarra.SelectedItem as double? ?? 12.0,
        RedondeoVarillas = ComboRedondeo.SelectedItem as double? ?? PlanTraslapos.RedondeoPorDefecto,
        SolapeMayorDeLosDos = Equals(ComboSolape.SelectedItem, TextoSolapeMayor),
        TipoGanchoLongitudinalNombre = ComboGanchoLongitudinal.SelectedItem as string,
        // Las posiciones de grapas/cajas no se leen del panel: se editan aparte
        // en L4 y viven en _longitudinalPorColumna hasta que este método las
        // pisa -- por eso Guardar... las vuelve a copiar explícitamente.
    };

    private void MostrarLongitudinalEnPanel(ConfigLongitudinal cfg)
    {
        _mostrandoLongitudinal = true;
        try
        {
            ComboBarrasX.SelectedItem = cfg.BarrasPorCaraX;
            ComboBarrasY.SelectedItem = cfg.BarrasPorCaraY;
            ComboBarraCaraX.SelectedItem = cfg.TipoBarraCaraXNombre;
            ComboBarraCaraY.SelectedItem = cfg.TipoBarraCaraYNombre;
            ComboBarraEsquinas.SelectedItem = cfg.TipoBarraEsquinasNombre;
            ComboLongitudBarra.SelectedItem = cfg.LongitudComercialBarra;

            // Un redondeo que no está en la lista (configuración editada a mano)
            // se muestra como el por defecto, en vez de dejar el combo vacío.
            ComboRedondeo.SelectedItem = PlanTraslapos.Redondeos.Contains(cfg.RedondeoVarillas)
                ? cfg.RedondeoVarillas
                : PlanTraslapos.RedondeoPorDefecto;
            ComboSolape.SelectedItem = cfg.SolapeMayorDeLosDos ? TextoSolapeMayor : TextoSolapeCadaDiametro;
            ComboGanchoLongitudinal.SelectedItem = cfg.TipoGanchoLongitudinalNombre;

            TextoEstribosMenores.Text = ResumenEstribosMenores(cfg);
        }
        finally
        {
            _mostrandoLongitudinal = false;
        }

        RecalcularTraslapos();
    }

    /// <summary>
    /// Vuelve a calcular y dibujar los traslapos de la columna activa con lo
    /// que hay ahora en el panel. Es el mismo cálculo que usa el generador
    /// (<see cref="PlanTraslapos"/>), así que lo que se ve es lo que se genera.
    /// </summary>
    private void RecalcularTraslapos()
    {
        if (_columnaActiva is null)
        {
            PanelDeTraslapos.MostrarMensaje("Elige una columna para ver sus traslapos.");
            return;
        }

        // Mismo anillo que el generador: las posiciones pares son A y las
        // impares B, y cada una lleva la barra de su cara.
        ConfigLongitudinal cfg = LeerLongitudinalDelPanel();
        List<double?> diametros = cfg.Anillo()
            .Select(p => _barras.FirstOrDefault(b => b.Nombre == cfg.BarraDe(p))?.DiametroMm)
            .ToList();
        if (diametros.Any(d => d is null))
        {
            PanelDeTraslapos.MostrarMensaje("Elige la barra de las esquinas, de la cara X y de la cara Y para ver los traslapos.");
            return;
        }

        PlanTraslapos.Resultado plan = PlanTraslapos.Calcular(
            PlanTraslapos.Columna.De(_columnaActiva),
            diametros.Where((_, i) => i % 2 == 0).Select(d => d!.Value),
            diametros.Where((_, i) => i % 2 == 1).Select(d => d!.Value),
            new PlanTraslapos.Opciones(cfg.LongitudComercialBarra, cfg.RedondeoVarillas, cfg.SolapeMayorDeLosDos));
        PanelDeTraslapos.Mostrar(_columnaActiva, plan);
    }

    private static string ResumenEstribosMenores(ConfigLongitudinal cfg)
    {
        // Una configuración vieja todavía puede traer las casillas: se cuentan
        // igual, para que el resumen no diga "sin estribos menores" de más.
        int grapas = cfg.EstribosMenores.Count(p => p.Forma != FormaMenor.Cerrado) + cfg.GrapasFila.Count + cfg.GrapasColumna.Count;
        int cerrados = cfg.EstribosMenores.Count(p => p.Forma == FormaMenor.Cerrado) + cfg.Cajas.Count;

        return grapas == 0 && cerrados == 0
            ? "Sin estribos menores configurados."
            : $"{grapas} grapa(s), {cerrados} estribo(s) cerrado(s) configurado(s).";
    }

    /// <summary>
    /// Lo que el panel de estribos menores necesita para marcar en rojo un
    /// estribo cerrado que no se puede doblar: la distancia real entre barras
    /// intermedias vecinas, con la misma cuenta que el generador (las
    /// intermedias se reparten entre las esquinas, corridas el recubrimiento,
    /// el estribo y media barra longitudinal -- SeccionPlanta). null si todavía
    /// falta elegir alguna barra.
    /// </summary>
    private VentanaEstribosMenores.MedidasSeccion? MedidasParaEstribosMenores(string idColumna, int barrasX, int barrasY)
    {
        if (_columnaActiva is null) return null;

        TipoBarra? Barra(string? nombre) => _barras.FirstOrDefault(b => b.Nombre == nombre);
        string? estribo = ComboTipoBarra.SelectedItem as string;
        double? dEstribo = Barra(estribo)?.DiametroMm;
        double?[] longitudinales = { Barra(ComboBarraEsquinas.SelectedItem as string)?.DiametroMm,
                                     Barra(ComboBarraCaraX.SelectedItem as string)?.DiametroMm,
                                     Barra(ComboBarraCaraY.SelectedItem as string)?.DiametroMm };
        if (dEstribo is null || longitudinales.Any(d => d is null)) return null;

        double recubrimiento = _recubrimientoPorColumna.TryGetValue(idColumna, out double r) ? r : _columnaActiva.Recubrimiento;
        double inset = SeccionPlanta.Inset(recubrimiento, dEstribo.Value / 1000, longitudinales.Max()!.Value / 1000);
        double Paso(double lado, int intermedias) => (lado - 2 * inset) / (intermedias + 1);

        return new VentanaEstribosMenores.MedidasSeccion(
            Paso(_columnaActiva.BaseX, Math.Max(1, barrasX / 2)),
            Paso(_columnaActiva.BaseY, Math.Max(1, barrasY / 2)),
            Barra,
            estribo);
    }

    /// <summary>
    /// La sección de la columna activa para dibujarla a escala en el panel de
    /// estribos menores (#133, #134). Si falta elegir alguna barra se usa un
    /// diámetro de reserva (#4 las longitudinales, #3 el estribo) y el panel lo
    /// avisa: la forma de la sección ya se puede ver.
    /// </summary>
    private VentanaEstribosMenores.SeccionReal SeccionParaEstribosMenores(string idColumna, ColumnaContexto columna)
    {
        const double reservaLongitudinalMm = 12.7, reservaEstriboMm = 9.5;

        double? Diametro(object? nombre) => _barras.FirstOrDefault(b => b.Nombre == nombre as string)?.DiametroMm;
        double? esquina = Diametro(ComboBarraEsquinas.SelectedItem), caraX = Diametro(ComboBarraCaraX.SelectedItem),
            caraY = Diametro(ComboBarraCaraY.SelectedItem), estribo = Diametro(ComboTipoBarra.SelectedItem);

        return new VentanaEstribosMenores.SeccionReal(
            columna.BaseX, columna.BaseY,
            _recubrimientoPorColumna.TryGetValue(idColumna, out double r) ? r : columna.Recubrimiento,
            esquina ?? reservaLongitudinalMm, caraX ?? reservaLongitudinalMm, caraY ?? reservaLongitudinalMm, estribo ?? reservaEstriboMm,
            DiametrosDeReserva: esquina is null || caraX is null || caraY is null || estribo is null);
    }

    /// <summary>Las barras que se ofrecen para los estribos menores: las mismas aptas que para el estribo (#64).</summary>
    private IReadOnlyList<string> BarrasAptasParaEstribo() => BarrasAptas(_barras).Select(b => b.Nombre).ToList();

    /// <summary>Las barras aptas (o aptas con aviso) para estribo según el cuadro (#64).</summary>
    private static IReadOnlyList<TipoBarra> BarrasAptas(IEnumerable<TipoBarra> barras) => barras
        .Where(b => CuadroVarillas.EvaluarParaEstribo(b).Aptitud != AptitudEstribo.NoApta)
        .ToList();

    /// <summary>
    /// Debajo del combo de barra de estribo (#64): una línea de resumen y, plegado,
    /// el detalle -- las barras con aviso primero (son las que se pueden elegir),
    /// después las que quedaron fuera, con su motivo. Las que ni siquiera están
    /// en el cuadro se cuentan en una sola línea.
    /// </summary>
    private void MostrarNotaBarrasEstribo(IEnumerable<TipoBarra> barras)
    {
        var conAviso = new List<string>();
        var fuera = new List<string>();
        int fueraDelCuadro = 0;
        foreach (TipoBarra barra in barras)
        {
            (AptitudEstribo aptitud, string? motivo) = CuadroVarillas.EvaluarParaEstribo(barra);
            if (aptitud == AptitudEstribo.Apta || motivo is null) continue;
            if (CuadroVarillas.PorNombrePropio(barra.Nombre) is null && CuadroVarillas.PorDiametro(barra.DiametroMm) is null)
                fueraDelCuadro++;
            else if (aptitud == AptitudEstribo.NoApta)
                fuera.Add($"Fuera: {motivo}");
            else
                conAviso.Add($"Aviso: {motivo}");
        }

        if (fueraDelCuadro > 0) fuera.Add($"Fuera: {fueraDelCuadro} barra(s) que no están en el cuadro de la empresa.");
        int cuantasFuera = fuera.Count - (fueraDelCuadro > 0 ? 1 : 0) + fueraDelCuadro;

        var resumen = new List<string>();
        if (conAviso.Count > 0) resumen.Add($"{conAviso.Count} barra(s) con aviso");
        if (cuantasFuera > 0) resumen.Add($"{cuantasFuera} fuera de la lista");
        ResumenBarrasEstribo.Text = BarrasAptas(barras).Count == 0
            ? "Ninguna barra del modelo es apta para estribo: no se pueden generar estribos hasta corregir una en Revit (ver por qué)."
            : string.Join(" · ", resumen) + " (ver por qué)";

        DetalleBarrasEstribo.Text = string.Join("\n", conAviso.Concat(fuera));
        NotaBarrasEstribo.Visibility = conAviso.Count + fuera.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static ConfigLongitudinal ConfigLongitudinalPorDefecto(IProveedorTipos tipos) => new()
    {
        BarrasPorCaraX = 2,
        BarrasPorCaraY = 2,
        // Issue #60: "todo número cuatro por defecto" (William, 11-sep).
        TipoBarraCaraXNombre = BarraPorDefecto(tipos, NumeroVarillaLongitudinal),
        TipoBarraCaraYNombre = BarraPorDefecto(tipos, NumeroVarillaLongitudinal),
        TipoBarraEsquinasNombre = BarraPorDefecto(tipos, NumeroVarillaLongitudinal),
        LongitudComercialBarra = 12.0,
        // Anclaje en la zapata: estándar de 90°, también de la reunión del 10-sep.
        TipoGanchoLongitudinalNombre = TipoGancho.PorDefecto(tipos.ListarGanchos(), EstiloGancho.Estandar, 90)?.Nombre,
    };

    /// <summary>
    /// Lo del panel MÁS los estribos menores de la columna activa. Las
    /// posiciones de grapas y cajas no se editan en este panel (se editan
    /// aparte, en L4) y viven en <see cref="_longitudinalPorColumna"/>, así que
    /// leer solo el panel las perdería. Lo usan tanto el guardado por columna
    /// como las configuraciones con nombre.
    /// </summary>
    private ConfigLongitudinal LeerLongitudinalConEstribosMenores()
    {
        ConfigLongitudinal nuevo = LeerLongitudinalDelPanel();

        if (_columnaIdActiva is string idColumna &&
            _longitudinalPorColumna.TryGetValue(idColumna, out ConfigLongitudinal? previo))
        {
            previo.MigrarEstribosMenores();
            nuevo.EstribosMenores = previo.EstribosMenores;
        }

        return nuevo;
    }

    private void GuardarLongitudinalDeColumnaActiva()
    {
        if (_columnaActiva is null || _columnaIdActiva is not string idColumna) return;

        ConfigLongitudinal cfg = LeerLongitudinalConEstribosMenores();

        // Si se bajó la cantidad de barras por cara, las piezas que apuntaban
        // a una fila o columna que ya no existe se quitan, avisando.
        int descartadas = cfg.DescartarMenoresFueraDeRango();
        if (descartadas > 0)
            TextoEstado.Text = $"Se quitaron {descartadas} estribo(s) menor(es) que apuntaban a barras que ya no existen con la cantidad de barras elegida.";

        _longitudinalPorColumna[idColumna] = cfg;
    }

    /// <summary>
    /// Llena el combo con los recubrimientos distintos que tienen las piezas
    /// de la columna activa (más el normal del equipo) y deja elegido el de la
    /// columna -- el que ya tenía, o si es la primera vez que se activa, el
    /// del tramo activo.
    /// </summary>
    private void CargarRecubrimientoDeColumnaActiva()
    {
        if (_columnaActiva is null || _columnaIdActiva is not string idColumna) return;

        static double Redondear(double metros) => Math.Round(metros, 3);

        if (!_recubrimientoPorColumna.TryGetValue(idColumna, out double elegido))
        {
            double delTramoActivo = _columnaActiva.Tramos.ElementAtOrDefault(_tramoActivoIndice)?.Recubrimiento ?? 0;
            elegido = Redondear(delTramoActivo > 0 ? delTramoActivo
                : _columnaActiva.Recubrimiento > 0 ? _columnaActiva.Recubrimiento
                : RecubrimientoNormal);
            _recubrimientoPorColumna[idColumna] = elegido;
        }

        List<OpcionRecubrimiento> opciones = _columnaActiva.Tramos
            .Select(t => t.Recubrimiento)
            .Where(r => r > 0)
            .Append(RecubrimientoNormal)
            .Append(elegido)
            .Select(Redondear)
            .Distinct()
            .OrderBy(r => r)
            .Select(r => new OpcionRecubrimiento(r))
            .ToList();

        _cargandoRecubrimiento = true;
        try
        {
            ComboRecubrimiento.ItemsSource = opciones;
            ComboRecubrimiento.SelectedItem = opciones.First(o => o.Metros == elegido);
        }
        finally
        {
            _cargandoRecubrimiento = false;
        }
    }

    private void ComboRecubrimiento_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_cargandoRecubrimiento || _columnaIdActiva is not string idColumna) return;
        if (ComboRecubrimiento.SelectedItem is OpcionRecubrimiento opcion)
            _recubrimientoPorColumna[idColumna] = opcion.Metros;
    }

    private void CargarLongitudinalDeColumnaActiva()
    {
        if (_columnaActiva is null || _columnaIdActiva is not string idColumna) return;

        ConfigLongitudinal cfg = _longitudinalPorColumna.TryGetValue(idColumna, out ConfigLongitudinal? existente)
            ? existente
            : ConfigLongitudinalPorDefecto(_tipos);

        MostrarLongitudinalEnPanel(cfg);
    }

    private void BotonEstribosMenores_Click(object sender, RoutedEventArgs e)
    {
        if (_columnaActiva is null || _columnaIdActiva is not string idColumna) return;

        int barrasX = ComboBarrasX.SelectedItem as int? ?? 0;
        int barrasY = ComboBarrasY.SelectedItem as int? ?? 0;

        if (barrasX <= 0 || barrasY <= 0)
        {
            TextoEstado.Text = "Elige N. barras X y N. barras Y antes de editar los estribos menores.";
            return;
        }

        _longitudinalPorColumna.TryGetValue(idColumna, out ConfigLongitudinal? actual);

        actual?.MigrarEstribosMenores();

        var dialogo = new VentanaEstribosMenores(
            barrasX, barrasY,
            actual?.EstribosMenores ?? new List<EstriboMenor>(),
            BarrasAptasParaEstribo(),
            SeccionParaEstribosMenores(idColumna, _columnaActiva),
            MedidasParaEstribosMenores(idColumna, barrasX, barrasY))
        { Owner = this };

        if (dialogo.ShowDialog() != true) return;

        GuardarLongitudinalDeColumnaActiva();
        ConfigLongitudinal cfg = _longitudinalPorColumna[idColumna];
        cfg.EstribosMenores = dialogo.Piezas;

        TextoEstribosMenores.Text = ResumenEstribosMenores(cfg);
    }

    // ------------------------------------------------------------- aceptar

    /// <summary>
    /// Los tramos de la columna activa que pasan el filtro (o todos, con
    /// <paramref name="filtro"/> en <c>null</c>). El contexto de la columna se
    /// lee UNA vez: los conteos de los botones y lo que después se acepta salen
    /// de acá, así que no pueden discrepar.
    /// </summary>
    private List<NodoLista> TramosDeLaColumnaActiva(Func<Tramo, bool>? filtro)
    {
        if (_columnaActiva is null || _columnaIdActiva is not string idColumna) return new List<NodoLista>();

        return _nodos
            .Where(n => !n.EsColumna && n.ColumnaId == idColumna)
            .Where(n => n.TramoIndice >= 0 && n.TramoIndice < _columnaActiva.Tramos.Count)
            .Where(n => filtro is null || filtro(_columnaActiva.Tramos[n.TramoIndice]))
            .ToList();
    }

    private void BotonAceptar_Click(object sender, RoutedEventArgs e) => Aceptar(extenderA: null);

    private void BotonAceptarMismoTipo_Click(object sender, RoutedEventArgs e)
    {
        if (_columnaActiva is null) return;

        TipoTramo tipo = _columnaActiva.Tramos[_tramoActivoIndice].Tipo;
        Aceptar(t => t.Tipo == tipo);
    }

    private void BotonAceptarColumna_Click(object sender, RoutedEventArgs e) => Aceptar(_ => true);

    /// <summary>
    /// El gesto central de la ventana desde el issue #56: acepta el tramo
    /// activo -- y, si <paramref name="extenderA"/> no es null, también a los
    /// tramos de la columna que pasen ese filtro (issue #57).
    ///
    /// "Aceptado" es lo que decide el chulito y lo que se genera, y es lo único
    /// que hace entrar algo en <see cref="_tramosAceptados"/>: navegar hasta un
    /// tramo o teclear en un campo no alcanza, a propósito.
    ///
    /// Con varias columnas elegidas le cae a todas las que compartan estructura
    /// con la activa, y las que no quedan nombradas en el estado (issue #63).
    /// </summary>
    private void Aceptar(Func<Tramo, bool>? extenderA)
    {
        if (_columnaActiva is null || _columnaIdActiva is null) return;

        ConfigTramo plantilla = LeerConfigDelPanel();
        Tramo tramoActivo = _columnaActiva.Tramos[_tramoActivoIndice];

        // Se valida ANTES de aceptar nada: aceptar es decir "esto va a Revit
        // así", y no tiene sentido marcar con chulito algo que el generador va
        // a rebotar.
        List<string> errores = Validar(plantilla, tramoActivo);
        if (errores.Count > 0)
        {
            TextoValidacion.Text = string.Join(Environment.NewLine, errores);
            TextoEstado.Text = "No se aceptó nada: falta corregir lo que dice arriba en rojo.";
            return;
        }

        (List<(string Id, ColumnaContexto Ctx)> compatibles, List<string> descartadas) = ColumnasCompatibles();

        // Qué índices de tramo se tocan. Solo el activo, o el activo más los
        // que pasen el filtro -- los índices valen para todas las columnas
        // compatibles justamente porque comparten estructura.
        var indices = new HashSet<int> { _tramoActivoIndice };
        if (extenderA is not null)
        {
            foreach (NodoLista nodo in TramosDeLaColumnaActiva(extenderA)) indices.Add(nodo.TramoIndice);
        }

        var conProblemaDeNorma = new List<string>();
        int aceptados = 0;

        foreach ((string idColumna, ColumnaContexto ctx) in compatibles)
        {
            // El acero longitudinal es por columna, no por tramo, pero también
            // forma parte de "la misma configuración para varias columnas" que
            // pidió William (issue #63): sin esto, las columnas que no son la
            // activa se generaban con el longitudinal por defecto en vez de con
            // el que está en el panel. Copia propia -- si compartieran las
            // listas de grapas y cajas, editarlas en una las cambiaría en todas.
            _longitudinalPorColumna[idColumna] = LeerLongitudinalConEstribosMenores().Copiar();

            // El recubrimiento elegido también es parte de "la misma
            // configuración para varias columnas": se lleva a las que todavía
            // no tienen uno propio, sin pisar el que alguien ya eligió.
            if (_columnaIdActiva is string idActivaParaRecubrimiento &&
                _recubrimientoPorColumna.TryGetValue(idActivaParaRecubrimiento, out double recubrimientoActivo))
                _recubrimientoPorColumna.TryAdd(idColumna, recubrimientoActivo);

            foreach (int indice in indices)
            {
                // Copia independiente por tramo -- no una referencia compartida.
                // Editar uno solo después (con las flechas) no le tiene que
                // cambiar el valor a los demás que recibieron esta misma config.
                _configPorTramo[(idColumna, indice)] = new ConfigTramo
                {
                    Modo = plantilla.Modo,
                    TipoBarraNombre = plantilla.TipoBarraNombre,
                    TipoGanchoNombre = plantilla.TipoGanchoNombre,
                    EspaciadoUniforme = plantilla.EspaciadoUniforme,
                    LongitudConfinamiento = plantilla.LongitudConfinamiento,
                    EspaciadoConfinado = plantilla.EspaciadoConfinado,
                    EspaciadoNoConfinado = plantilla.EspaciadoNoConfinado,
                };

                _tramosAceptados.Add((idColumna, indice));
                aceptados++;

                // El chequeo de L1 vs altura sí depende de CADA tramo: distintas
                // columnas y distintos tramos tienen distinta altura, y eso es
                // exactamente lo que William dijo que no debía impedir compartir
                // la configuración. Se avisa, no se bloquea -- la geometría
                // incoherente de verdad la ataja el generador.
                Tramo tramoReal = ctx.Tramos[indice];

                if (plantilla.Modo == ModoEstribo.Confinado
                    && tramoReal.Altura > 0
                    && plantilla.LongitudConfinamiento * 2 >= tramoReal.Altura)
                {
                    conProblemaDeNorma.Add($"{ctx.Marca}[{indice}]");
                }
            }
        }

        var mensaje = new List<string>
        {
            compatibles.Count == 1
                ? $"Aceptado: {aceptados} tramo(s) de {compatibles[0].Ctx.Marca}."
                : $"Aceptado: {aceptados} tramo(s) en {compatibles.Count} columnas ({string.Join(", ", compatibles.Select(c => c.Ctx.Marca))}).",
        };

        if (descartadas.Count > 0)
            mensaje.Add($"Quedaron afuera {string.Join(", ", descartadas)}: " +
                        $"{(descartadas.Count == 1 ? "no tiene" : "no tienen")} la misma estructura que " +
                        $"{_columnaActiva.Marca} ({CuantosTramos(_columnaActiva.Tramos.Count)}).");

        if (conProblemaDeNorma.Count > 0)
            mensaje.Add($"Atención: en {string.Join(", ", conProblemaDeNorma)} L1 x2 ≥ la altura del tramo -- " +
                        "revisa esos, no queda zona central.");

        TextoEstado.Text = string.Join(" ", mensaje);

        RefrescarChulitos();
        RedibujarElevacion();
        RedibujarPanelPlanta();
        ActualizarResumenDeGeneracion();
    }

    /// <summary>
    /// Saca un tramo de la generación. El chulito no es una casilla que haya que
    /// tildar (eso es justamente lo que pidió sacar William), pero tiene que
    /// haber forma de deshacer uno puesto por error: se clickea el ✓ en la
    /// elevación. La configuración del tramo NO se borra -- queda cargada por si
    /// se lo vuelve a aceptar.
    /// </summary>
    private void QuitarAceptacion(int indiceTramo)
    {
        if (_columnaIdActiva is not string idColumna) return;
        if (!_tramosAceptados.Remove((idColumna, indiceTramo))) return;

        TextoEstado.Text = $"Tramo {indiceTramo} de {_columnaActiva?.Marca} sacado de la generación " +
                           "(su configuración queda cargada).";

        RefrescarChulitos();
        RedibujarElevacion();
        RedibujarPanelPlanta();
        ActualizarResumenDeGeneracion();
    }

    /// <summary>
    /// Pasa <see cref="_tramosAceptados"/> a los <see cref="NodoLista"/>, que es
    /// lo que muestra la lista de columnetas. Una fila de columna lleva chulito
    /// si alguno de sus tramos está aceptado.
    /// </summary>
    private void RefrescarChulitos()
    {
        HashSet<string> columnasConAceptados = _tramosAceptados.Select(t => t.ColumnaId).ToHashSet();

        foreach (NodoLista nodo in _nodos)
        {
            nodo.Aceptado = nodo.EsColumna
                ? columnasConAceptados.Contains(nodo.ColumnaId)
                : _tramosAceptados.Contains((nodo.ColumnaId, nodo.TramoIndice));
        }
    }

    /// <summary>
    /// Sin las casillas de tildar hay que decir en números qué se va a armar; si
    /// no, el botón de generar es una caja negra.
    /// </summary>
    private void ActualizarResumenDeGeneracion()
    {
        int columnas = _tramosAceptados.Select(t => t.ColumnaId).Distinct().Count();
        int tramos = _tramosAceptados.Count;

        TextoResumenGeneracion.Text = tramos == 0
            ? "Todavía no hay nada aceptado: configura un tramo y aprieta Aceptar."
            : $"Listo para generar: {columnas} columna(s), {tramos} tramo(s) con ✓.";
    }

    // -------------------------------------------------------------- dibujo

    /// <summary>
    /// Dibuja la columna entera como un contorno continuo (no bloques
    /// sueltos), con los niveles a la izquierda unidos por una línea guía
    /// punteada -- mismo lenguaje visual que usa la ventana de referencia de
    /// William. Las bandas de color se superponen al contorno donde
    /// corresponde: nudo/zapata cruzando más ancho que la columna (como lo
    /// hace una viga real), el tramo activo en verde, y los tramos
    /// que están ACEPTADOS con un chulito verde a la izquierda (todavía no hay
    /// geometría real creada en Revit -- ese ✓ es de "esto se va a generar", no
    /// de "ya está generado"). El chulito se clickea para sacarlo.
    /// </summary>
    private void RedibujarElevacion()
    {
        CanvasElevacion.Children.Clear();
        if (_columnaActiva is null) return;

        const double margenSuperior = 24;
        const double margenInferior = 24;
        const double xColumna = 172;
        const double sobresaleNudo = 16;
        const double anchoEtiqueta = 130;
        const double xLeaderIni = 138;
        double xLeaderFin = xColumna - 4;
        // La columna crece hacia la derecha: a la izquierda están los rótulos
        // de nivel y los chulitos.
        double anchoMaximo = CanvasElevacion.Width - xColumna - sobresaleNudo - 6;

        double zMin = _columnaActiva.ZInferior;
        double zMax = _columnaActiva.ZSuperior;
        foreach (NivelRotulo nivel in _columnaActiva.Niveles)
        {
            zMin = Math.Min(zMin, nivel.Cota);
            zMax = Math.Max(zMax, nivel.Cota);
        }

        double alturaDisponible = Math.Max(1, CanvasElevacion.Height - margenSuperior - margenInferior);
        double rango = Math.Max(0.01, zMax - zMin);
        // El ancho a la misma escala que el alto (#133): la columna se ve con
        // su ancho real (el lado X, como "de frente" en la vista previa).
        double escala = EscalaDibujo.Ajustar(_columnaActiva.BaseX, rango, anchoMaximo, alturaDisponible);
        double anchoColumna = Math.Max(2, _columnaActiva.BaseX * escala);
        double Y(double cota) => margenSuperior + (zMax - cota) * escala;

        // 1) Contorno continuo de la columna, de punta a punta.
        var contorno = new Rectangle
        {
            Width = anchoColumna,
            Height = Math.Max(1, Y(_columnaActiva.ZInferior) - Y(_columnaActiva.ZSuperior)),
            Stroke = Paleta.Contorno,
            StrokeThickness = 1.5,
            Fill = Paleta.Superficie,
        };
        Canvas.SetLeft(contorno, xColumna);
        Canvas.SetTop(contorno, Y(_columnaActiva.ZSuperior));
        CanvasElevacion.Children.Add(contorno);
        Cotas.Horizontal(CanvasElevacion, xColumna, xColumna + anchoColumna, Y(_columnaActiva.ZInferior) + 6,
            EscalaDibujo.Cm(_columnaActiva.BaseX));

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
                Stroke = Paleta.Borde,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 1, 2 },
                IsHitTestVisible = false,
            };
            CanvasElevacion.Children.Add(linea);

            var texto = new TextBlock
            {
                Text = $"N{(nivel.Cota >= 0 ? "+" : "")}{nivel.Cota:0.00}  {nivel.Nombre}",
                FontSize = 10,
                Foreground = Paleta.TextoSecundario,
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
            bool aceptado = _columnaIdActiva is not null && _tramosAceptados.Contains((_columnaIdActiva, i));

            if (tramo.Tipo is TipoTramo.DentroDeViga or TipoTramo.ContraZapata)
            {
                Color color = tramo.Tipo == TipoTramo.DentroDeViga
                    ? Paleta.ColorNudo
                    : Paleta.ColorZapata;

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

            // El chulito, por fuera del contorno y a la izquierda: adentro ya
            // conviven la banda del nudo y el verde del tramo activo, y un tramo
            // puede ser las tres cosas a la vez. Va con un tinte verde suave
            // sobre el tramo para que "lo que se genera" se lea de un vistazo
            // en toda la columna, no leyendo ✓ por ✓.
            if (aceptado)
            {
                var tinte = new Rectangle
                {
                    Width = anchoColumna,
                    Height = alto,
                    Fill = new SolidColorBrush(Paleta.ConAlfa(Paleta.ColorExito, 30)),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(tinte, xColumna);
                Canvas.SetTop(tinte, yTop);
                CanvasElevacion.Children.Add(tinte);

                int indiceDelChulito = i;
                var chulito = new TextBlock
                {
                    Text = "✓",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = Paleta.Exito,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Este tramo se genera. Clic para sacarlo.",
                    Background = Brushes.Transparent,
                };
                Canvas.SetLeft(chulito, xColumna - sobresaleNudo - 18);
                Canvas.SetTop(chulito, yTop + Math.Max(0, alto / 2 - 9));
                chulito.MouseLeftButtonUp += (_, _) => QuitarAceptacion(indiceDelChulito);
                CanvasElevacion.Children.Add(chulito);
            }

            if (activo)
            {
                var resalte = new Rectangle
                {
                    Width = anchoColumna + 6,
                    Height = alto,
                    Fill = new SolidColorBrush(Paleta.ConAlfa(Paleta.ColorExitoClaro, 60)),
                    Stroke = Paleta.ExitoClaro,
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

    // ------------------------------------------------------------ generar

    /// <summary>
    /// Solo con lo ACEPTADO (issue #56) -- generar sobre todas las columnas del
    /// modelo, rellenando con la config por defecto lo que nadie tocó, inundaba
    /// de avisos irrelevantes y armaba de más sin que nadie lo pidiera. Un tramo
    /// no aceptado queda en <c>null</c> en <see cref="ColumnaPlan.Tramos"/> (ver
    /// su comentario), no con un default inventado; una columna sin ningún tramo
    /// aceptado ni siquiera entra en <see cref="PlanArmado.Columnas"/>.
    ///
    /// El acero LONGITUDINAL no se decide tramo por tramo: es por columna y va
    /// de punta a punta siempre. Una columna genera su longitudinal si tiene al
    /// menos un tramo aceptado.
    /// </summary>
    private PlanArmado ConstruirPlanArmado()
    {
        GuardarPanelEnTramoActivo();
        GuardarLongitudinalDeColumnaActiva();

        ILookup<string, int> aceptadosPorColumna = _tramosAceptados
            .ToLookup(t => t.ColumnaId, t => t.Indice);

        var plan = new PlanArmado();

        foreach (string id in _nodos.Where(n => n.EsColumna).Select(n => n.ColumnaId))
        {
            HashSet<int> aceptados = aceptadosPorColumna[id].ToHashSet();
            if (aceptados.Count == 0) continue;

            ColumnaContexto contexto = _contexto.Leer(id);
            var tramos = new List<ConfigTramo?>();

            for (int i = 0; i < contexto.Tramos.Count; i++)
            {
                tramos.Add(aceptados.Contains(i)
                    ? (_configPorTramo.TryGetValue((id, i), out ConfigTramo? cfg) ? cfg : ConfigPorDefecto(_tipos))
                    : null);
            }

            ConfigLongitudinal longitudinal = _longitudinalPorColumna.TryGetValue(id, out ConfigLongitudinal? l)
                ? l
                : ConfigLongitudinalPorDefecto(_tipos);

            plan.Columnas.Add(new ColumnaPlan
            {
                Id = id, Contexto = contexto, Tramos = tramos, Longitudinal = longitudinal,
                Recubrimiento = _recubrimientoPorColumna.TryGetValue(id, out double recubrimiento) ? recubrimiento : null,
            });
        }

        return plan;
    }

    private void BotonGenerar_Click(object sender, RoutedEventArgs e)
    {
        PlanArmado plan = ConstruirPlanArmado();
        if (plan.Columnas.Count == 0)
        {
            TextoEstado.Text = "Configura un tramo y aprieta \"Aceptar\" antes de generar: se arma lo que tiene ✓.";
            return;
        }

        var opciones = new OpcionesArmado
        {
            Plan = plan,
            BorrarAnteriores = CheckBorrarAnteriores.IsChecked == true,
        };

        TextoEstado.Text = "Generando armado en Revit…";
        BotonGenerar.IsEnabled = false;

        GenerarSolicitado?.Invoke(opciones);
    }

    /// <summary>
    /// Llamado por <c>ComandoEstribos</c> cuando la generación (#5) terminó --
    /// re-habilita el botón y deja un resumen en <see cref="TextoEstado"/>. Los
    /// avisos van aparte en un <see cref="MessageBox"/> (WPF puro, sin
    /// Autodesk.Revit) para no perderlos entre el resto del texto de estado.
    /// </summary>
    public void MostrarResultadoGeneracion(ResultadoArmado resultado)
    {
        BotonGenerar.IsEnabled = true;

        // La generación modificó el documento (creó y/o borró Rebar) -- la
        // geometría que _contexto tenía cacheada de antes ya no es válida.
        // Ver el comentario de IProveedorContexto.InvalidarCache: sin esto,
        // el próximo Aceptar o click en la planta puede
        // terminar en un crash nativo de Revit en vez de una excepción.
        _contexto.InvalidarCache();

        TextoEstado.Text =
            $"Generado: {resultado.GruposCreados} estribo(s) creado(s)" +
            (resultado.Eliminados > 0 ? $", {resultado.Eliminados} anterior(es) borrado(s)" : "") +
            (resultado.TramosOmitidos > 0 ? $", {resultado.TramosOmitidos} tramo(s) omitido(s)" : "") +
            ".";

        if (resultado.Avisos.Count > 0)
            MessageBox.Show(string.Join("\n\n", resultado.Avisos), "Avisos de la generación",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Llamado por <c>ComandoEstribos</c> si la generación tiró una excepción no anticipada.</summary>
    public void MostrarErrorGeneracion(string mensaje)
    {
        BotonGenerar.IsEnabled = true;

        // Aunque haya fallado, la corrida ya modificó el documento antes de
        // tirar la excepción (ver MostrarResultadoGeneracion) -- misma razón
        // para invalidar el cache.
        _contexto.InvalidarCache();

        TextoEstado.Text = "La generación falló.";
        MessageBox.Show(mensaje, "Error al generar", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static double LeerDouble(string texto) =>
        double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out double valor) ? valor : 0.0;

    private static string Formato(double valor) => valor.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// Fila de <c>ListaColumnetas</c> (issue #33) — una columna o uno de sus
/// tramos. Vive acá y no en Modelo/ porque es puro estado de presentación (el
/// ✓ que se ve en la lista), no algo que tenga sentido serializar en un
/// PlanArmado.
/// </summary>
public sealed class NodoLista : INotifyPropertyChanged
{
    public bool EsColumna { get; init; }

    /// <summary>Issue #33: a qué pestaña pertenece (heredado de <see cref="ColumnaResumen.EsColumneta"/> de su columna).</summary>
    public bool EsColumneta { get; init; }

    public string ColumnaId { get; init; } = "";
    public int TramoIndice { get; init; }
    public string Etiqueta { get; init; } = "";

    /// <summary>
    /// Solo en filas de tramo: el elemento de Revit que lo hostea (ver
    /// <see cref="Tramo.IdElementoHost"/>). Se guarda acá para no tener que
    /// releer el contexto de la columna entera cada vez que cambia una tilde.
    /// </summary>
    public string? IdElementoHost { get; init; }

    /// <summary>
    /// Si este tramo (o, en una fila de columna, alguno de sus tramos) está
    /// aceptado y por lo tanto se va a generar. La fuente de la verdad es
    /// <c>_tramosAceptados</c> en la ventana; esto es el espejo para la lista,
    /// y lo sincroniza <c>RefrescarChulitos</c>.
    /// </summary>
    private bool _aceptado;
    public bool Aceptado
    {
        get => _aceptado;
        set
        {
            if (_aceptado == value) return;
            _aceptado = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Aceptado)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Chulito)));
        }
    }

    /// <summary>El ✓ que muestra la lista -- vacío si no está aceptado.</summary>
    public string Chulito => _aceptado ? "✓" : "";

    public event PropertyChangedEventHandler? PropertyChanged;
}
