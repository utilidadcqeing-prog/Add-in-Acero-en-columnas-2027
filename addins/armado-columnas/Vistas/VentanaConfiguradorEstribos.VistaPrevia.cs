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

        double? Diametro(string? nombre) =>
            string.IsNullOrEmpty(nombre) ? null : _barras.FirstOrDefault(b => b.Nombre == nombre)?.DiametroMm;

        ConfigTramo cfgTramo = LeerConfigDelPanel();

        // Las longitudinales se corren con el estribo más grueso de la columna,
        // como en GeneradorLongitudinal.
        double estriboMasGrueso = _configPorTramo
            .Where(p => p.Key.ColumnaId == idColumna)
            .Select(p => Diametro(p.Value.TipoBarraNombre) ?? 0)
            .Append(Diametro(cfgTramo.TipoBarraNombre) ?? 0)
            .Max();

        double recubrimiento = _recubrimientoPorColumna.TryGetValue(idColumna, out double r) ? r : _columnaActiva.Recubrimiento;

        PanelDeVistaPrevia.Mostrar(
            _columnaActiva, _columnaActiva.Tramos[_tramoActivoIndice], cfgTramo, LeerLongitudinalConEstribosMenores(),
            recubrimiento, Diametro, estriboMasGrueso);
    }
}
