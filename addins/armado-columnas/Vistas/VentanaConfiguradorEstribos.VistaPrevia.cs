namespace practicacq.Vistas;

using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;
using practicacq.Modelo;

/// <summary>
/// La pestaña «Vista previa» de la ventana (docs/planes/2026-10-20-diseno-vista-previa-y-colores.md).
/// Va en su propio archivo para no mezclarse con el resto de la ventana: se
/// engancha a los mismos campos que ya existen y se redibuja sola.
/// </summary>
public partial class VentanaConfiguradorEstribos
{
    private bool _vistaPreviaPendiente;

    /// <summary>Se llama una vez, desde el constructor.</summary>
    private void ConectarVistaPrevia()
    {
        foreach (TextBox campo in new[] { CampoEspaciadoUniforme, CampoL1, CampoEspaciadoConfinado, CampoEspaciadoNoConfinado })
            campo.TextChanged += (_, _) => PedirVistaPrevia();

        foreach (ComboBox combo in new[]
                 {
                     ComboTipoBarra, ComboBarraCaraX, ComboBarraCaraY, ComboBarraEsquinas, ComboBarrasX, ComboBarrasY, ComboRecubrimiento,
                 })
            combo.SelectionChanged += (_, _) => PedirVistaPrevia();

        RadioConfinado.Checked += (_, _) => PedirVistaPrevia();
        RadioConfinado.Unchecked += (_, _) => PedirVistaPrevia();

        // Cambio de tramo o de columna (el rótulo «x / N» cambia) y estribos
        // menores editados (cambia su resumen): se escuchan los textos en vez
        // de tocar los métodos que los escriben.
        foreach (TextBlock texto in new[] { TextoTramoActivo, TextoEstribosMenores })
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
                .AddValueChanged(texto, (_, _) => PedirVistaPrevia());

        ConectarVista3D();
        PedirVistaPrevia();
    }

    /// <summary>
    /// Junta los cambios que llegan seguidos (cargar un tramo dispara muchos
    /// eventos) en un solo redibujo, cuando el panel ya terminó de llenarse.
    /// </summary>
    private void PedirVistaPrevia()
    {
        if (_vistaPreviaPendiente) return;
        _vistaPreviaPendiente = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _vistaPreviaPendiente = false;
            RecalcularVistaPrevia();
        }));
    }

    private void RecalcularVistaPrevia()
    {
        if (_columnaActiva is null || _columnaIdActiva is not string idColumna ||
            _tramoActivoIndice < 0 || _tramoActivoIndice >= _columnaActiva.Tramos.Count)
        {
            PanelDeVistaPrevia.MostrarMensaje("Elige una columna para ver la vista previa.");
            return;
        }

        (ConfigTramo cfgTramo, double recubrimiento, double estriboMasGrueso) = EntradasDeLaSeccion(idColumna, _columnaActiva);

        PanelDeVistaPrevia.Mostrar(
            _columnaActiva, _columnaActiva.Tramos[_tramoActivoIndice], cfgTramo, LeerLongitudinalConEstribosMenores(),
            recubrimiento, BarraPorNombre, estriboMasGrueso);

        // Los mismos cambios actualizan la vista 3D, si está abierta (#123).
        PedirVista3D(abrirVista: false);
    }

    private TipoBarra? BarraPorNombre(string? nombre) => _barras.FirstOrDefault(b => b.Nombre == nombre);

    /// <summary>
    /// Lo que la sección de la columna activa necesita además de la columna y
    /// el tramo: la configuración del panel, el recubrimiento y el estribo más
    /// grueso de la columna (las longitudinales se corren con ese, como en
    /// GeneradorLongitudinal). Lo usan la vista previa y los choques de
    /// "Editar estribos menores" (#131).
    /// </summary>
    private (ConfigTramo CfgTramo, double Recubrimiento, double EstriboMasGrueso) EntradasDeLaSeccion(
        string idColumna, ColumnaContexto columna)
    {
        double? Diametro(string? nombre) => string.IsNullOrEmpty(nombre) ? null : BarraPorNombre(nombre)?.DiametroMm;

        ConfigTramo cfgTramo = LeerConfigDelPanel();
        double estriboMasGrueso = _configPorTramo
            .Where(p => p.Key.ColumnaId == idColumna)
            .Select(p => Diametro(p.Value.TipoBarraNombre) ?? 0)
            .Append(Diametro(cfgTramo.TipoBarraNombre) ?? 0)
            .Max();
        double recubrimiento = _recubrimientoPorColumna.TryGetValue(idColumna, out double r) ? r : columna.Recubrimiento;
        return (cfgTramo, recubrimiento, estriboMasGrueso);
    }

    /// <summary>
    /// Cómo calcula sus choques "Editar estribos menores" (#131): la sección
    /// de la columna activa con las piezas que se están editando. Si el tramo
    /// activo es un nudo o el de la zapata (sin menores), se usa el primer
    /// tramo libre de la columna. null si no hay ninguno.
    /// </summary>
    private Func<IReadOnlyList<EstriboMenor>, ChoquesSeccion.Resultado>? ChoquesParaEstribosMenores()
    {
        if (_columnaActiva is not ColumnaContexto columna || _columnaIdActiva is not string idColumna) return null;

        Tramo? activo = _tramoActivoIndice >= 0 && _tramoActivoIndice < columna.Tramos.Count ? columna.Tramos[_tramoActivoIndice] : null;
        Tramo? tramo = activo is { Tipo: TipoTramo.Libre } ? activo : columna.Tramos.FirstOrDefault(t => t.Tipo == TipoTramo.Libre);
        if (tramo is null) return null;

        (ConfigTramo cfgTramo, double recubrimiento, double estriboMasGrueso) = EntradasDeLaSeccion(idColumna, columna);
        // Si se usa otro tramo, con SU estribo (lo guardado), no con el del panel.
        if (tramo != activo && _configPorTramo.TryGetValue((idColumna, tramo.Indice), out ConfigTramo? guardada))
            cfgTramo = guardada;
        ConfigLongitudinal longitudinal = LeerLongitudinalConEstribosMenores();
        return piezas =>
        {
            ConfigLongitudinal conEstas = longitudinal.Copiar();
            conEstas.GrapasFila = new List<int>();
            conEstas.GrapasColumna = new List<int>();
            conEstas.Cajas = new List<CajaMenor>();
            conEstas.EstribosMenores = piezas.ToList();
            return ChoquesSeccion.DeTramo(columna, tramo, cfgTramo, conEstas, recubrimiento, BarraPorNombre, estriboMasGrueso);
        };
    }
}
