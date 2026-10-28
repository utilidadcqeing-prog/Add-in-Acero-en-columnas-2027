namespace practicacq.Vistas;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using practicacq.Modelo;

/// <summary>
/// En qué está cada tramo respecto de la generación (#168). Es lo que dice el
/// ✓: verde, gris o ámbar.
/// </summary>
public enum EstadoAceptacion
{
    /// <summary>Ni aceptado ni generado: sin ✓.</summary>
    Ninguno,

    /// <summary>Aceptado y sin tocar desde entonces: entra en la próxima generación (✓ verde).</summary>
    Pendiente,

    /// <summary>
    /// Cambiado en el panel desde que se aceptó o se generó (✓ ámbar). Si
    /// estaba aceptado, Generar usa lo último aceptado, no lo tecleado.
    /// </summary>
    Editado,

    /// <summary>Ya generado y no vuelto a aceptar: no entra en la próxima generación (✓ gris).</summary>
    Generado,
}

/// <summary>
/// El ✓ como interruptor (#168): se saca con un clic en la elevación, la
/// planta o la lista; los botones de aceptar pasan a «Quitar ✓» cuando ya
/// está; «Quitar todos» con deshacer; después de generar, lo generado queda
/// en gris; si se edita algo aceptado queda en ámbar y se genera con lo
/// último aceptado; y al cerrar con tramos pendientes se pregunta.
/// </summary>
public partial class VentanaConfiguradorEstribos
{
    /// <summary>
    /// Lo que se aceptó en cada tramo, copia aparte de <see cref="_configPorTramo"/>
    /// (que sigue a lo tecleado). Es lo que se genera.
    /// </summary>
    private readonly Dictionary<(string ColumnaId, int Indice), ConfigTramo> _aceptadoComo = new();

    /// <summary>El longitudinal (con sus estribos menores) de la última vez que se aceptó algo en la columna.</summary>
    private readonly Dictionary<string, ConfigLongitudinal> _longitudinalAceptado = new();

    /// <summary>Lo que sacó «Quitar todos los ✓», para «Deshacer». Null si no hay nada que deshacer.</summary>
    private List<((string ColumnaId, int Indice) Tramo, ConfigTramo Config)>? _quitadosParaDeshacer;
    private Dictionary<string, ConfigLongitudinal>? _longitudinalesParaDeshacer;

    // ------------------------------------------------------------- estados

    /// <summary>El estado de un tramo, comparando lo que tiene ahora contra lo aceptado o lo generado.</summary>
    private EstadoAceptacion EstadoDe(string idColumna, int indice)
    {
        if (_aceptadoComo.TryGetValue((idColumna, indice), out ConfigTramo? aceptado) && _tramosAceptados.Contains((idColumna, indice)))
            return Coincide(idColumna, indice, aceptado, _longitudinalAceptado.GetValueOrDefault(idColumna))
                ? EstadoAceptacion.Pendiente
                : EstadoAceptacion.Editado;

        if (_generadas.TryGetValue(idColumna, out ColumnaGenerada? generada) &&
            indice < generada.Tramos.Count && generada.Tramos[indice] is ConfigTramo generado)
            return Coincide(idColumna, indice, generado, generada.Longitudinal)
                ? EstadoAceptacion.Generado
                : EstadoAceptacion.Editado;

        return EstadoAceptacion.Ninguno;
    }

    /// <summary>Uno solo por columna: ámbar si alguno está editado, si no verde, si no gris.</summary>
    private EstadoAceptacion EstadoDeColumna(string idColumna, int cuantosTramos)
    {
        List<EstadoAceptacion> estados = Enumerable.Range(0, cuantosTramos).Select(i => EstadoDe(idColumna, i)).ToList();
        if (estados.Contains(EstadoAceptacion.Editado)) return EstadoAceptacion.Editado;
        if (estados.Contains(EstadoAceptacion.Pendiente)) return EstadoAceptacion.Pendiente;
        return estados.Contains(EstadoAceptacion.Generado) ? EstadoAceptacion.Generado : EstadoAceptacion.Ninguno;
    }

    private bool Coincide(string idColumna, int indice, ConfigTramo tramo, ConfigLongitudinal? longitudinal) =>
        ComparadorConfig.Igual(ConfigActualDe(idColumna, indice), tramo) &&
        (longitudinal is null || ComparadorConfig.Igual(Normalizado(LongitudinalActualDe(idColumna)), Normalizado(longitudinal)));

    /// <summary>Lo que tiene el tramo ahora: el panel si es el activo, si no lo guardado al navegar.</summary>
    private ConfigTramo ConfigActualDe(string idColumna, int indice) =>
        idColumna == _columnaIdActiva && indice == _tramoActivoIndice && _columnaActiva is not null
            ? LeerConfigDelPanel()
            : _configPorTramo.TryGetValue((idColumna, indice), out ConfigTramo? cfg) ? cfg : ConfigPorDefecto(_tipos);

    private ConfigLongitudinal LongitudinalActualDe(string idColumna) =>
        idColumna == _columnaIdActiva && _columnaActiva is not null
            ? LeerLongitudinalConEstribosMenores()
            : _longitudinalPorColumna.TryGetValue(idColumna, out ConfigLongitudinal? cfg) ? cfg : ConfigLongitudinalPorDefecto(_tipos);

    /// <summary>Copia con los estribos menores en el formato actual: una configuración vieja no tiene que salir "cambiada".</summary>
    private static ConfigLongitudinal Normalizado(ConfigLongitudinal cfg)
    {
        ConfigLongitudinal copia = cfg.Copiar();
        copia.MigrarEstribosMenores();
        return copia;
    }

    internal static Brush ColorDe(EstadoAceptacion estado) => estado switch
    {
        EstadoAceptacion.Editado => Paleta.AvisoBorde,
        EstadoAceptacion.Generado => Paleta.TextoApagado,
        _ => Paleta.Exito,
    };

    private static string AyudaDe(EstadoAceptacion estado, bool aceptado) => estado switch
    {
        EstadoAceptacion.Pendiente => "Este tramo se genera. Clic para sacarlo.",
        EstadoAceptacion.Editado when aceptado =>
            "Cambiaste algo desde que lo aceptaste: Generar usa lo último aceptado. Vuelve a aceptar para llevar los cambios, o clic para sacarlo.",
        EstadoAceptacion.Editado => "Cambiaste algo desde que se generó: acéptalo para volver a generarlo.",
        EstadoAceptacion.Generado => "Ya generado. No se vuelve a generar salvo que lo aceptes otra vez.",
        _ => "",
    };

    /// <summary>
    /// Vuelve a pintar todo lo que depende del estado de los ✓: la lista, la
    /// elevación, la planta, los botones y el resumen. Lo llama la pasada de
    /// <see cref="PedirEstadoPanel"/> (cualquier cambio del panel) y cada gesto
    /// que acepta o quita.
    /// </summary>
    private void RefrescarEstadoAceptacion()
    {
        Dictionary<string, int> tramosPorColumna = _nodos.Where(n => !n.EsColumna)
            .GroupBy(n => n.ColumnaId).ToDictionary(g => g.Key, g => g.Count());

        foreach (NodoLista nodo in _nodos)
            nodo.Estado = nodo.EsColumna
                ? EstadoDeColumna(nodo.ColumnaId, tramosPorColumna.GetValueOrDefault(nodo.ColumnaId))
                : EstadoDe(nodo.ColumnaId, nodo.TramoIndice);

        RedibujarElevacion();
        RedibujarPanelPlanta();
        if (_columnaActiva is not null && _tramoActivoIndice < _columnaActiva.Tramos.Count)
            ActualizarBotonesDeAceptar(_columnaActiva.Tramos[_tramoActivoIndice]);
        ActualizarResumenDeGeneracion();
    }

    // -------------------------------------------------------------- quitar

    /// <summary>
    /// Los tramos (índices) que tocaría un gesto de aceptar o quitar: los
    /// mismos en todas las columnas elegidas que comparten estructura con la
    /// activa, como <see cref="Aceptar"/> (issue #63).
    /// </summary>
    private List<(string ColumnaId, int Indice)> TramosDelGesto(Func<Tramo, bool>? extenderA)
    {
        if (_columnaActiva is null) return new List<(string, int)>();

        var indices = new HashSet<int> { _tramoActivoIndice };
        if (extenderA is not null)
            foreach (NodoLista nodo in TramosDeLaColumnaActiva(extenderA)) indices.Add(nodo.TramoIndice);

        return ColumnasCompatibles().Compatibles
            .SelectMany(c => indices.Select(i => (c.Id, i)))
            .ToList();
    }

    /// <summary>Si todos los tramos del gesto ya están aceptados y sin cambios: el botón pasa a «Quitar ✓».</summary>
    private bool TodosPendientes(Func<Tramo, bool>? extenderA) =>
        TramosDelGesto(extenderA) is { Count: > 0 } tramos &&
        tramos.All(t => EstadoDe(t.ColumnaId, t.Indice) == EstadoAceptacion.Pendiente);

    /// <summary>Saca de la generación los tramos del gesto (el par de cada botón de aceptar).</summary>
    private void QuitarDelGesto(Func<Tramo, bool>? extenderA)
    {
        List<(string ColumnaId, int Indice)> tramos = TramosDelGesto(extenderA).Where(_tramosAceptados.Contains).ToList();
        Quitar(tramos);

        int columnas = tramos.Select(t => t.ColumnaId).Distinct().Count();
        TextoEstado.Text = $"Se quitó el ✓ de {tramos.Count} tramo(s)" + (columnas > 1 ? $" en {columnas} columnas" : "") +
                           " (su configuración queda cargada).";
    }

    private void Quitar(IEnumerable<(string ColumnaId, int Indice)> tramos)
    {
        foreach ((string ColumnaId, int Indice) tramo in tramos.ToList())
        {
            _tramosAceptados.Remove(tramo);
            _aceptadoComo.Remove(tramo);
        }
        foreach (string id in _longitudinalAceptado.Keys.Where(id => !_tramosAceptados.Any(t => t.ColumnaId == id)).ToList())
            _longitudinalAceptado.Remove(id);

        OlvidarDeshacer();
        RefrescarEstadoAceptacion();
    }

    /// <summary>Clic en el ✓ de una columna, en la planta o en la lista: saca la columna entera.</summary>
    private void QuitarColumna(string idColumna)
    {
        List<(string ColumnaId, int Indice)> tramos = _tramosAceptados.Where(t => t.ColumnaId == idColumna).ToList();
        if (tramos.Count == 0) return;

        Quitar(tramos);
        string marca = _nodos.FirstOrDefault(n => n.EsColumna && n.ColumnaId == idColumna)?.Marca ?? idColumna;
        TextoEstado.Text = $"Se quitó el ✓ de {marca} ({tramos.Count} tramo(s); su configuración queda cargada).";
    }

    private void BotonQuitarTodos_Click(object sender, RoutedEventArgs e)
    {
        if (_tramosAceptados.Count == 0) return;

        var quitados = _tramosAceptados.Select(t => (t, _aceptadoComo[t])).ToList();
        var longitudinales = new Dictionary<string, ConfigLongitudinal>(_longitudinalAceptado);
        Quitar(_tramosAceptados.ToList());

        _quitadosParaDeshacer = quitados;
        _longitudinalesParaDeshacer = longitudinales;
        BotonDeshacerQuitar.Visibility = Visibility.Visible;
        TextoEstado.Text = $"Se quitaron {quitados.Count} tramo(s) con ✓.";
    }

    private void BotonDeshacerQuitar_Click(object sender, RoutedEventArgs e)
    {
        if (_quitadosParaDeshacer is null || _longitudinalesParaDeshacer is null) return;

        foreach (((string ColumnaId, int Indice) tramo, ConfigTramo config) in _quitadosParaDeshacer)
        {
            _tramosAceptados.Add(tramo);
            _aceptadoComo[tramo] = config;
        }
        foreach ((string id, ConfigLongitudinal longitudinal) in _longitudinalesParaDeshacer)
            _longitudinalAceptado[id] = longitudinal;

        TextoEstado.Text = $"Se volvieron a poner {_quitadosParaDeshacer.Count} ✓.";
        OlvidarDeshacer();
        RefrescarEstadoAceptacion();
    }

    private void OlvidarDeshacer()
    {
        _quitadosParaDeshacer = null;
        _longitudinalesParaDeshacer = null;
        BotonDeshacerQuitar.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// El ✓ de la lista de columnetas: en una fila de columna saca la columna
    /// entera; en una de tramo, ese tramo. Se maneja al bajar el botón y se
    /// marca como resuelto, para que el clic no cambie además la selección.
    /// </summary>
    private void ChulitoLista_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: NodoLista nodo }) return;
        if (nodo.Estado is not (EstadoAceptacion.Pendiente or EstadoAceptacion.Editado)) return;

        e.Handled = true;
        if (nodo.EsColumna) QuitarColumna(nodo.ColumnaId);
        else if (_tramosAceptados.Contains((nodo.ColumnaId, nodo.TramoIndice)))
        {
            Quitar(new[] { (nodo.ColumnaId, nodo.TramoIndice) });
            TextoEstado.Text = $"Tramo {nodo.TramoIndice + 1} sacado de la generación (su configuración queda cargada).";
        }
    }

    // ------------------------------------------------------ generar / cerrar

    /// <summary>Después de generar: lo que estaba aceptado pasa a "ya generado" (gris).</summary>
    private void MarcarGenerados(PlanArmado plan)
    {
        var generados = plan.Columnas
            .SelectMany(c => c.Tramos.Select((t, i) => (Tramo: (c.Id, i), Pedido: t is not null)))
            .Where(x => x.Pedido)
            .Select(x => x.Tramo)
            .ToList();
        Quitar(generados);
    }

    /// <summary>Cuántos tramos están aceptados y todavía no se generaron.</summary>
    private int Pendientes => _tramosAceptados.Count;

    private void AlCerrar(object? sender, CancelEventArgs e)
    {
        if (Pendientes == 0) return;

        e.Cancel = MessageBox.Show(this,
            $"Hay {Pendientes} tramo(s) aceptado(s) sin generar. Si cierras, se pierden. ¿Cerrar igual?",
            "Tramos sin generar", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes;
    }
}
