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
    /// referencia compartida) incluso cuando llegó por "Aplicar a selección".
    /// </summary>
    private readonly Dictionary<(string ColumnaId, int Indice), ConfigTramo> _configPorTramo = new();

    /// <summary>
    /// Una copia de <see cref="ConfigLongitudinal"/> por columna (no por
    /// tramo: el acero longitudinal corre toda la altura de la columna). L4
    /// vive del lado de sus grapas/cajas (<see cref="ConfigLongitudinal.GrapasFila"/>,
    /// <see cref="ConfigLongitudinal.GrapasColumna"/>, <see cref="ConfigLongitudinal.Cajas"/>)
    /// dentro de este mismo tipo.
    /// </summary>
    private readonly Dictionary<string, ConfigLongitudinal> _longitudinalPorColumna = new();

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
    /// En alto mientras se resalta en la lista por código (desde la planta o
    /// desde una selección hecha en Revit). Sin esto, cada elemento agregado a
    /// <c>SelectedItems</c> volvería a entrar por
    /// <see cref="ListaSeleccion_SelectionChanged"/> y activaría un tramo a
    /// mitad de camino.
    /// </summary>
    private bool _resaltandoEnLista;

    /// <summary>De qué columna es lo que hoy muestra el panel de tramos.</summary>
    private string? _columnaDeTramosCargada;

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
    /// Se dispara con los ids de TODOS los elementos cuyo tramo está tildado en
    /// la lista, cada vez que cambia una tilde -- puede venir vacía (nada
    /// tildado). A diferencia de <see cref="ElementosActivados"/>, quien lo
    /// escucha NO debe mover la cámara: es solo reflejar en Revit qué está
    /// marcado para "Aplicar a selección", sin interrumpir al usuario mirando
    /// otra cosa.
    /// </summary>
    public event Action<IReadOnlyList<string>>? SeleccionMarcadaCambio;

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

        ComboTipoBarra.ItemsSource = _tipos.ListarTiposBarra();
        ComboBarraCaraX.ItemsSource = _tipos.ListarTiposBarra();
        ComboBarraCaraY.ItemsSource = _tipos.ListarTiposBarra();
        ComboBarraEsquinas.ItemsSource = _tipos.ListarTiposBarra();

        // Cada combo de gancho ve SOLO los de su estilo: en Revit un gancho
        // solo se puede aplicar a barras de su mismo estilo, así que ofrecer
        // los dos mezclados es ofrecer ganchos que fallan recién al generar.
        // Los dos combos están ocultos (el valor se fija por defecto, reunión
        // del 10-sep) pero se llenan igual, porque de ahí los lee el panel.
        IReadOnlyList<TipoGancho> ganchos = _tipos.ListarGanchos();
        ComboTipoGancho.ItemsSource = ganchos.Where(g => g.Estilo == EstiloGancho.Estribo).Select(g => g.Nombre).ToList();
        ComboGanchoLongitudinal.ItemsSource = ganchos.Where(g => g.Estilo == EstiloGancho.Estandar).Select(g => g.Nombre).ToList();

        AvisarSiFaltaAlgunGanchoPorDefecto(ganchos);

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

        RadioUniforme.Checked += (_, _) => { ActualizarVisibilidadModo(); RevalidarPanel(); };
        RadioConfinado.Checked += (_, _) => { ActualizarVisibilidadModo(); RevalidarPanel(); };

        // Cableado acá y no en el XAML: si estos se disparan durante el
        // parseo inicial (InitializeComponent, al asignar los Text="..." del
        // propio XAML), los controles que tocan todavía no existen como
        // campos conectados y esto revienta con NullReferenceException --
        // pasó de verdad con CampoEspaciadoConfinado_TextChanged, ver commit.
        CampoEspaciadoConfinado.TextChanged += CampoEspaciadoConfinado_TextChanged;
        CampoEspaciadoConfinado.TextChanged += (_, _) => RevalidarPanel();

        PanelPlanta.ColumnaClicked += ActivarYAlternarMarcadoDesdePlanta;
        PanelPlanta.ColumnasEnRectangulo += ElegirColumnasDesdeRectangulo;
        CampoEspaciadoUniforme.TextChanged += (_, _) => RevalidarPanel();
        CampoL1.TextChanged += (_, _) => RevalidarPanel();
        CampoEspaciadoNoConfinado.TextChanged += CampoEspaciadoNoConfinado_TextChanged;
        CampoEspaciadoNoConfinado.TextChanged += (_, _) => RevalidarPanel();
        ComboTipoBarra.SelectionChanged += (_, _) => RevalidarPanel();
        ComboTipoGancho.SelectionChanged += (_, _) => RevalidarPanel();

        RefrescarConfiguracionesGuardadas();
        CargarColumnas();
    }

    // -------------------------------------------------------- lista / árbol

    private void CargarColumnas()
    {
        _nodos.Clear();

        foreach (ColumnaResumen resumen in _contexto.ListarColumnas())
            AgregarColumnaYTramos(resumen);

        // Issue #33: dos pestañas en vez de una sola lista -- cada ListBox
        // muestra un subconjunto de los mismos NodoLista (comparten
        // instancia, así que tildar en una pestaña no desincroniza nada de
        // lo que ya recorre _nodos completo). La pestaña de columnetas se
        // oculta si el modelo no tiene ninguna, para no agregar una pestaña
        // vacía de más.
        ListaPrincipales.ItemsSource = _nodos.Where(n => !n.EsColumneta).ToList();
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

    /// <summary>Handler compartido por las dos pestañas (issue #33) -- cada una es un ListBox distinto.</summary>
    private void ListaSeleccion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Si esto lo disparó SincronizarDesdeRevit (al poner lista.SelectedItem
        // = nodo, unas líneas más abajo de ahí), ese método ya llama a
        // Activar(nodo) explícitamente después -- sin este corte se llama
        // Activar dos veces reentrante, una desde acá en medio de la otra,
        // pisando _columnaActiva/_tramoActivoIndice a mitad de camino. Eso
        // dejaba el estado inconsistente y probablemente era la causa real
        // del crash de Revit al cerrar la ventana (una excepción ahí se
        // escapa por el lado nativo del evento de selección de Revit).
        if (_sincronizandoDesdeRevit || _resaltandoEnLista) return;
        if (sender is not ListBox lista) return;

        // El resaltado es uno solo, se vea desde donde se vea: la lista de
        // afuera, el panel de tramos y la elevación muestran lo mismo.
        ReflejarResaltadoEnLasOtrasListas(lista);

        // Con varios resaltados NO se cambia el tramo activo: resaltar es
        // elegir a quién le va a caer "Aplicar a selección", no navegar. Si
        // se activara uno por cada Ctrl+clic, además, la vista de Revit se
        // movería una vez por clic (ElementosActivados centra la cámara).
        if (lista.SelectedItems.Count == 1 && lista.SelectedItem is NodoLista nodo) Activar(nodo);
        else RedibujarElevacion();
    }

    /// <summary>
    /// Tildar/destildar una columna cascadea a todos sus tramos. Tildar un
    /// tramo suelto no toca a los demás ni a su columna — los dos niveles de
    /// selección son independientes, como quedó confirmado en el issue.
    /// </summary>
    private void CheckBoxNodo_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: NodoLista nodo }) return;

        if (nodo.EsColumna)
        {
            foreach (NodoLista tramo in _nodos.Where(n => !n.EsColumna && n.ColumnaId == nodo.ColumnaId))
                tramo.Marcado = nodo.Marcado;
        }

        NotificarSeleccionMarcada();
    }

    /// <summary>
    /// Junta los elementos de todos los tramos tildados (la cascada de arriba
    /// ya se encargó de que tildar una columna tilde a sus tramos) y avisa --
    /// puede salir vacía si se destildó todo, y eso también hay que avisarlo
    /// para que Revit deseleccione.
    /// </summary>
    private void NotificarSeleccionMarcada()
    {
        List<string> ids = _nodos
            .Where(n => !n.EsColumna && n.Marcado && n.IdElementoHost is not null)
            .Select(n => n.IdElementoHost!)
            .Distinct()
            .ToList();

        SeleccionMarcadaCambio?.Invoke(ids);
    }

    // ------------------------------------------------------- tramo activo

    private void Activar(NodoLista nodo)
    {
        GuardarPanelEnTramoActivo();
        GuardarLongitudinalDeColumnaActiva();

        _columnaActiva = _contexto.Leer(nodo.ColumnaId);
        _columnaIdActiva = nodo.ColumnaId;
        _tramoActivoIndice = nodo.EsColumna ? 0 : nodo.TramoIndice;
        _modoColumnaCompleta = nodo.EsColumna;

        RedibujarElevacion();
        CargarPanelDelTramoActivo();
        CargarLongitudinalDeColumnaActiva();
        MostrarAvisos();
        NotificarElementosActivos();

        // Resaltar en la planta la misma columna que acaba de activarse -- por
        // cualquier camino: clic en la lista, clic en el propio panel, flechas
        // de la elevación, o sincronización desde una selección hecha en Revit.
        // Si su pestaña no está a la vista el panel mide cero y no dibuja nada,
        // así que no hace falta preguntarlo.
        RedibujarPanelPlanta();
        CargarPanelDeTramos();
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
    /// Elegir una configuración solo CARGA el panel -- no toca ningún tramo.
    /// Aplicarla a los tramos es el otro gesto ("Aplicar a selección"), a
    /// propósito: así se puede mirar qué trae antes de pisar nada.
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
        if (_columnaIdActiva is string idColumna) _longitudinalPorColumna[idColumna] = longitudinal;
        MostrarLongitudinalEnPanel(longitudinal);

        CampoNombreConfiguracion.Text = nombre;
        RevalidarPanel();

        string menores = ResumenEstribosMenores(longitudinal);
        TextoEstado.Text = $"Configuración «{nombre}» cargada en el panel ({menores.ToLowerInvariant().TrimEnd('.')}). " +
                           "Para llevarla a los tramos, resáltalos y usa \"Aplicar a selección\".";
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

    private void BotonTildarTodosLosTramos_Click(object sender, RoutedEventArgs e) => TildarTodosLosTramos(true);

    private void BotonDestildarTodosLosTramos_Click(object sender, RoutedEventArgs e) => TildarTodosLosTramos(false);

    /// <summary>
    /// Tilda (o destilda) de una todos los tramos de la columna activa --
    /// generar la columna entera es el caso más común y hacerlo tramo por
    /// tramo era un clic por piso. Toca también la fila de la columna, para
    /// que su casilla no quede contando otra cosa.
    ///
    /// Solo el TILDE, que es lo que decide qué se genera. Para elegir a quién
    /// le cae "Aplicar a selección" está el resaltado (y ahí vale Ctrl+A, que
    /// es de la lista).
    /// </summary>
    private void TildarTodosLosTramos(bool marcado)
    {
        if (_columnaIdActiva is not string idColumna) return;

        List<NodoLista> deLaColumna = _nodos.Where(n => n.ColumnaId == idColumna).ToList();
        foreach (NodoLista nodo in deLaColumna) nodo.Marcado = marcado;

        NotificarSeleccionMarcada();
        RedibujarPanelPlanta();
        RedibujarElevacion();

        int tramos = deLaColumna.Count(n => !n.EsColumna);
        string marca = _columnaActiva?.Marca ?? idColumna;
        TextoEstado.Text = marcado
            ? $"Tildados los {tramos} tramos de {marca}."
            : $"Destildados los {tramos} tramos de {marca}.";
    }

    /// <summary>
    /// El panel del medio: los tramos de la columna activa, con su tilde.
    /// Son los MISMOS <see cref="NodoLista"/> que ya están en la lista de la
    /// izquierda (comparten instancia), así que tildar acá o allá es lo mismo.
    /// </summary>
    private void CargarPanelDeTramos()
    {
        if (_columnaIdActiva is not string idColumna)
        {
            ListaTramos.ItemsSource = null;
            TextoColumnaDeTramos.Text = "Tramos";
            return;
        }

        List<NodoLista> tramos = _nodos.Where(n => !n.EsColumna && n.ColumnaId == idColumna).ToList();

        // Solo se rearma la lista cuando cambió de columna: rearmarla en cada
        // clic perdería el resaltado múltiple que el usuario acaba de hacer
        // acá adentro.
        if (_columnaDeTramosCargada != idColumna)
        {
            ListaTramos.ItemsSource = tramos;
            _columnaDeTramosCargada = idColumna;
        }
        TextoColumnaDeTramos.Text = _columnaActiva is null
            ? "Tramos"
            : $"Tramos de {_columnaActiva.Marca}";

        // El tramo activo queda resaltado acá también, para que se vea dónde
        // está parado el panel de la derecha -- salvo que ya forme parte de un
        // resaltado múltiple, que en ese caso no hay que deshacer.
        if (tramos.FirstOrDefault(n => n.TramoIndice == _tramoActivoIndice) is { } activo &&
            !ListaTramos.SelectedItems.Contains(activo))
        {
            _resaltandoEnLista = true;
            try { ListaTramos.SelectedItem = activo; }
            finally { _resaltandoEnLista = false; }
        }
    }

    private void RedibujarPanelPlanta()
    {
        HashSet<string> marcadas = _nodos.Where(n => n.EsColumna && n.Marcado).Select(n => n.ColumnaId).ToHashSet();
        PanelPlanta.Cargar(_contexto.ListarColumnas(), _contexto.ListarRejillas(), _columnaIdActiva, marcadas);
    }

    /// <summary>
    /// Clic en una columna del selector visual de planta: activa Y tilda (o
    /// destilda) la columna entera de un solo gesto -- pedido explícito del
    /// usuario, mismo efecto combinado que activar la fila en negrita de la
    /// lista y tildar su checkbox. Cascadea a todos sus tramos, igual que
    /// <see cref="CheckBoxNodo_Changed"/>.
    /// </summary>
    private void ActivarYAlternarMarcadoDesdePlanta(string columnaId, bool sumar)
    {
        NodoLista? nodo = _nodos.FirstOrDefault(n => n.EsColumna && n.ColumnaId == columnaId);
        if (nodo is null) return;

        nodo.Marcado = !nodo.Marcado;
        foreach (NodoLista tramo in _nodos.Where(n => !n.EsColumna && n.ColumnaId == nodo.ColumnaId))
            tramo.Marcado = nodo.Marcado;
        NotificarSeleccionMarcada();

        // Además del tilde, queda RESALTADA: es a lo resaltado que le cae
        // "Aplicar a selección" (ver TramosResaltados). Con Ctrl se suma a lo
        // que ya estaba elegido en vez de reemplazarlo.
        ResaltarEnLista(new[] { nodo }, sumar);

        Activar(nodo);
    }

    /// <summary>
    /// Arrastre sobre la planta (reunión 10-sep): tilda y resalta todas las
    /// columnas alcanzadas. A diferencia del clic suelto no alterna -- las
    /// deja elegidas, que es lo que uno espera de una selección por
    /// rectángulo. Para destildar en masa está "Limpiar selección".
    /// </summary>
    private void ElegirColumnasDesdeRectangulo(IReadOnlyList<string> idsColumnas, bool sumar)
    {
        var alcanzadas = new HashSet<string>(idsColumnas);
        List<NodoLista> columnas = _nodos.Where(n => n.EsColumna && alcanzadas.Contains(n.ColumnaId)).ToList();
        if (columnas.Count == 0) return;

        foreach (NodoLista n in _nodos.Where(n => alcanzadas.Contains(n.ColumnaId)))
            n.Marcado = true;
        NotificarSeleccionMarcada();

        ResaltarEnLista(columnas, sumar);
        Activar(columnas[0]);
    }

    /// <summary>
    /// Resalta filas por código. Hay tres <see cref="ListBox"/> mostrando los
    /// mismos nodos (las dos pestañas de columnas, issue #33, y el panel de
    /// tramos), así que cada nodo se resalta en todos los que lo tengan. Con
    /// <paramref name="sumar"/> en falso se reemplaza lo que hubiera.
    /// </summary>
    private void ResaltarEnLista(IEnumerable<NodoLista> nodos, bool sumar)
    {
        _resaltandoEnLista = true;
        try
        {
            if (!sumar)
            {
                ListaPrincipales.SelectedItems.Clear();
                ListaColumnetas.SelectedItems.Clear();
                ListaTramos.SelectedItems.Clear();
            }

            foreach (NodoLista nodo in nodos)
            {
                ListBox destino = nodo.EsColumneta ? ListaColumnetas : ListaPrincipales;
                if (!destino.SelectedItems.Contains(nodo)) destino.SelectedItems.Add(nodo);

                if (ListaTramos.Items.Contains(nodo) && !ListaTramos.SelectedItems.Contains(nodo))
                    ListaTramos.SelectedItems.Add(nodo);
            }
        }
        finally
        {
            _resaltandoEnLista = false;
        }

        RedibujarElevacion();
    }

    /// <summary>
    /// Lo resaltado en una lista se refleja en las otras dos: son vistas de los
    /// mismos nodos, así que resaltar un tramo en el panel del medio también lo
    /// resalta en la lista de afuera, y al revés.
    /// </summary>
    private void ReflejarResaltadoEnLasOtrasListas(ListBox origen)
    {
        var seleccionados = origen.SelectedItems.Cast<NodoLista>().ToHashSet();

        _resaltandoEnLista = true;
        try
        {
            foreach (ListBox otra in new[] { ListaPrincipales, ListaColumnetas, ListaTramos })
            {
                if (ReferenceEquals(otra, origen)) continue;

                otra.SelectedItems.Clear();
                foreach (NodoLista nodo in otra.Items.OfType<NodoLista>().Where(seleccionados.Contains))
                    otra.SelectedItems.Add(nodo);
            }
        }
        finally
        {
            _resaltandoEnLista = false;
        }
    }

    /// <summary>
    /// Los índices de tramo resaltados de la columna que se está mirando --
    /// para pintarlos en la elevación. Resaltar la fila de la columna entera
    /// vale por todos sus tramos, igual que para "Aplicar a selección".
    /// </summary>
    private HashSet<int> IndicesResaltadosDeLaColumnaActiva()
    {
        if (_columnaIdActiva is not string idColumna) return new HashSet<int>();

        return TramosResaltados()
            .Where(n => n.ColumnaId == idColumna)
            .Select(n => n.TramoIndice)
            .ToHashSet();
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

            ResaltarEnLista(coinciden, sumar: false);
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
        CargarPanelDeTramos();
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
    private void AvisarSiFaltaAlgunGanchoPorDefecto(IReadOnlyList<TipoGancho> ganchos)
    {
        var faltan = new List<string>();

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

    private static ConfigTramo ConfigPorDefecto(IProveedorTipos tipos) => new()
    {
        TipoBarraNombre = tipos.ListarTiposBarra().FirstOrDefault(),
        // Estribo/tirante de 135°, confirmado por William el 10-sep. Antes era
        // "el primero alfabéticamente", que no es un criterio.
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
        LongitudComercialBarra = ComboLongitudBarra.SelectedItem as double? ?? 12.0,
        TipoGanchoLongitudinalNombre = ComboGanchoLongitudinal.SelectedItem as string,
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
        ComboLongitudBarra.SelectedItem = cfg.LongitudComercialBarra;
        ComboGanchoLongitudinal.SelectedItem = cfg.TipoGanchoLongitudinalNombre;

        TextoEstribosMenores.Text = ResumenEstribosMenores(cfg);
    }

    private static string ResumenEstribosMenores(ConfigLongitudinal cfg)
    {
        int grapas = cfg.GrapasFila.Count + cfg.GrapasColumna.Count;
        int cajas = cfg.Cajas.Count;

        return grapas == 0 && cajas == 0
            ? "Sin estribos menores configurados."
            : $"{grapas} grapa(s), {cajas} caja(s) configurada(s).";
    }

    private static ConfigLongitudinal ConfigLongitudinalPorDefecto(IProveedorTipos tipos) => new()
    {
        BarrasPorCaraX = 2,
        BarrasPorCaraY = 2,
        TipoBarraCaraXNombre = tipos.ListarTiposBarra().FirstOrDefault(),
        TipoBarraCaraYNombre = tipos.ListarTiposBarra().FirstOrDefault(),
        TipoBarraEsquinasNombre = tipos.ListarTiposBarra().FirstOrDefault(),
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
            nuevo.GrapasFila = previo.GrapasFila;
            nuevo.GrapasColumna = previo.GrapasColumna;
            nuevo.Cajas = previo.Cajas;
        }

        return nuevo;
    }

    private void GuardarLongitudinalDeColumnaActiva()
    {
        if (_columnaActiva is null || _columnaIdActiva is not string idColumna) return;

        _longitudinalPorColumna[idColumna] = LeerLongitudinalConEstribosMenores();
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

        var dialogo = new VentanaEstribosMenores(
            barrasX, barrasY,
            actual?.GrapasFila ?? new List<int>(),
            actual?.GrapasColumna ?? new List<int>(),
            actual?.Cajas ?? new List<CajaMenor>())
        { Owner = this };

        if (dialogo.ShowDialog() != true) return;

        GuardarLongitudinalDeColumnaActiva();
        ConfigLongitudinal cfg = _longitudinalPorColumna[idColumna];
        cfg.GrapasFila = dialogo.GrapasFila;
        cfg.GrapasColumna = dialogo.GrapasColumna;
        cfg.Cajas = dialogo.Cajas;

        TextoEstribosMenores.Text = ResumenEstribosMenores(cfg);
    }

    // ----------------------------------------------------------- selección

    /// <summary>
    /// A qué tramos les cae "Aplicar a selección": a los RESALTADOS en la
    /// lista, no a los tildados.
    ///
    /// Antes iba a los tildados, y eso hacía las dos cosas con el mismo gesto:
    /// como para generar una columna entera hay que tildarla entera (ver
    /// <see cref="ConstruirPlanArmado"/>), un "Aplicar" pisaba la configuración
    /// individual de todos sus tramos. Le pasó a William en la reunión del
    /// 10-sep: configuró tramo por tramo #4, #3, #2... y salió todo con el
    /// mismo tipo de barra de punta a punta.
    ///
    /// Resaltar una columna (la fila en negrita) vale por todos sus tramos:
    /// es lo que uno espera al seleccionarla entera.
    /// </summary>
    private List<NodoLista> TramosResaltados()
    {
        var resaltados = ListaPrincipales.SelectedItems.Cast<NodoLista>()
            .Concat(ListaColumnetas.SelectedItems.Cast<NodoLista>())
            .Concat(ListaTramos.SelectedItems.Cast<NodoLista>())
            .ToList();

        HashSet<string> columnasEnteras = resaltados.Where(n => n.EsColumna).Select(n => n.ColumnaId).ToHashSet();

        return _nodos
            .Where(n => !n.EsColumna && (columnasEnteras.Contains(n.ColumnaId) || resaltados.Contains(n)))
            .ToList();
    }

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

        List<NodoLista> objetivo = TramosResaltados();
        if (objetivo.Count == 0)
        {
            TextoEstado.Text = "No hay nada resaltado: selecciona en la lista los tramos (o las columnas) a los que quieras " +
                               "cargarles esta configuración -- con Ctrl o Shift para varios. El tilde es otra cosa: decide qué se genera.";
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
              "L1 x2 ≥ la altura del tramo -- revisa esos, no queda zona central.";

        // Si el tramo activo estaba entre los que se acaban de tocar, refleja
        // el resultado en el panel en vez de dejarlo desactualizado.
        if (_columnaActiva is not null
            && objetivo.Any(n => n.ColumnaId == _columnaIdActiva && n.TramoIndice == _tramoActivoIndice))
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
        HashSet<int> resaltados = IndicesResaltadosDeLaColumnaActiva();

        for (int i = 0; i < _columnaActiva.Tramos.Count; i++)
        {
            Tramo tramo = _columnaActiva.Tramos[i];
            double yTop = Y(tramo.ZSuperior);
            double alto = Math.Max(1, Y(tramo.ZInferior) - yTop);
            bool activo = i == _tramoActivoIndice;
            bool resaltado = resaltados.Contains(i);
            bool configurado = _columnaIdActiva is not null && _configPorTramo.ContainsKey((_columnaIdActiva, i));

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

            // Lo resaltado se pinta como una barra al costado, por fuera del
            // contorno: adentro ya conviven el nudo, la trama de configurado y
            // el verde del tramo activo, y un tramo puede ser las cuatro cosas
            // a la vez.
            if (resaltado)
            {
                var barra = new Rectangle
                {
                    Width = 6,
                    Height = alto,
                    Fill = new SolidColorBrush(Color.FromRgb(0x0B, 0x63, 0xCE)),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(barra, xColumna - sobresaleNudo - 10);
                Canvas.SetTop(barra, yTop);
                CanvasElevacion.Children.Add(barra);

                var tinte = new Rectangle
                {
                    Width = anchoColumna,
                    Height = alto,
                    Fill = new SolidColorBrush(Color.FromArgb(38, 0x0B, 0x63, 0xCE)),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(tinte, xColumna);
                Canvas.SetTop(tinte, yTop);
                CanvasElevacion.Children.Add(tinte);
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

    // ------------------------------------------------------------ generar

    /// <summary>
    /// Solo con lo tildado en la lista -- generar sobre TODAS las
    /// columnas del modelo (rellenando con la config por defecto lo que
    /// nadie tocó) inundaba de avisos irrelevantes y armaba de más sin que
    /// nadie lo pidiera. Un tramo no tildado queda en <c>null</c> en
    /// <see cref="ColumnaPlan.Tramos"/> (ver su comentario), no con un
    /// default inventado; una columna sin ningún tramo tildado ni siquiera
    /// entra en <see cref="PlanArmado.Columnas"/>.
    /// </summary>
    private PlanArmado ConstruirPlanArmado()
    {
        GuardarPanelEnTramoActivo();
        GuardarLongitudinalDeColumnaActiva();

        ILookup<string, int> tramosMarcadosPorColumna = _nodos
            .Where(n => !n.EsColumna && n.Marcado)
            .ToLookup(n => n.ColumnaId, n => n.TramoIndice);

        var plan = new PlanArmado();

        foreach (string id in _nodos.Where(n => n.EsColumna).Select(n => n.ColumnaId))
        {
            HashSet<int> marcados = tramosMarcadosPorColumna[id].ToHashSet();
            if (marcados.Count == 0) continue;

            ColumnaContexto contexto = _contexto.Leer(id);
            var tramos = new List<ConfigTramo?>();

            for (int i = 0; i < contexto.Tramos.Count; i++)
            {
                tramos.Add(marcados.Contains(i)
                    ? (_configPorTramo.TryGetValue((id, i), out ConfigTramo? cfg) ? cfg : ConfigPorDefecto(_tipos))
                    : null);
            }

            ConfigLongitudinal longitudinal = _longitudinalPorColumna.TryGetValue(id, out ConfigLongitudinal? l)
                ? l
                : ConfigLongitudinalPorDefecto(_tipos);

            plan.Columnas.Add(new ColumnaPlan { Id = id, Contexto = contexto, Tramos = tramos, Longitudinal = longitudinal });
        }

        return plan;
    }

    private void BotonGenerar_Click(object sender, RoutedEventArgs e)
    {
        PlanArmado plan = ConstruirPlanArmado();
        if (plan.Columnas.Count == 0)
        {
            TextoEstado.Text = "Tilda al menos un tramo (o una columna entera) antes de generar.";
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

    private void BotonLimpiarSeleccion_Click(object sender, RoutedEventArgs e)
    {
        // Asignación directa, no vía la cascada de CheckBoxNodo_Changed: la
        // ListBox virtualiza filas fuera de pantalla, así que tocar la
        // propiedad de un nodo no realizado no dispara Checked/Unchecked en
        // ningún control real. Se notifica una sola vez al final en vez de
        // depender de eso.
        foreach (NodoLista nodo in _nodos)
            nodo.Marcado = false;

        NotificarSeleccionMarcada();
        TextoEstado.Text = "Selección limpiada.";
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
        // el próximo "Aplicar a selección" o click en la lista puede
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
/// Fila de <c>ListaPrincipales</c>/<c>ListaColumnetas</c> (issue #33) — una
/// columna o uno de sus tramos. Vive acá y no en Modelo/ porque es puro
/// estado de presentación (el checkbox tildado), no algo que tenga sentido
/// serializar en un PlanArmado.
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
