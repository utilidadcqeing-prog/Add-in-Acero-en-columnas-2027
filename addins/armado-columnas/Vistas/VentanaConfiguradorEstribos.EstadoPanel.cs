namespace practicacq.Vistas;

using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Threading;

/// <summary>
/// Lo que se recalcula cada vez que cambia algo del panel: si tiene cambios
/// sin guardar respecto de la configuración elegida (#179) y el color de los ✓
/// (ámbar si algo aceptado cambió, #168). Mismo esquema que
/// la vista previa (<c>VentanaConfiguradorEstribos.VistaPrevia.cs</c>): se
/// engancha a los campos que ya existen y junta los cambios seguidos en una
/// sola pasada.
/// </summary>
public partial class VentanaConfiguradorEstribos
{
    private bool _estadoPanelPendiente;

    /// <summary>Se llama una vez, desde el constructor.</summary>
    private void ConectarEstadoPanel()
    {
        foreach (TextBox campo in new[] { CampoEspaciadoUniforme, CampoL1, CampoEspaciadoConfinado, CampoEspaciadoNoConfinado })
            campo.TextChanged += (_, _) => PedirEstadoPanel();

        foreach (ComboBox combo in new[]
                 {
                     ComboTipoBarra, ComboTipoGancho, ComboBarraCaraX, ComboBarraCaraY, ComboBarraEsquinas, ComboBarrasX, ComboBarrasY,
                     ComboLongitudBarra, ComboRedondeo, ComboSolape, ComboGanchoLongitudinal,
                 })
            combo.SelectionChanged += (_, _) => PedirEstadoPanel();

        RadioConfinado.Checked += (_, _) => PedirEstadoPanel();
        RadioConfinado.Unchecked += (_, _) => PedirEstadoPanel();

        // Cambio de tramo o de columna, y estribos menores editados.
        foreach (TextBlock texto in new[] { TextoTramoActivo, TextoEstribosMenores })
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
                .AddValueChanged(texto, (_, _) => PedirEstadoPanel());

        PedirEstadoPanel();
    }

    private void PedirEstadoPanel()
    {
        if (_estadoPanelPendiente) return;
        _estadoPanelPendiente = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _estadoPanelPendiente = false;
            ActualizarCambiosSinGuardar();
            RefrescarEstadoAceptacion();
        }));
    }
}
