namespace practicacq.Vistas;

using System;
using System.Collections.Generic;
using practicacq.Modelo;

/// <summary>
/// La vista previa 3D en Revit de la ventana (#123,
/// docs/planes/2026-10-21-vista-previa-3d-columnas-123.md, paso 6). Va en su
/// propio archivo, como la vista previa 2D: se engancha a
/// <see cref="RecalcularVistaPrevia"/>, así se actualiza con los mismos
/// cambios. La ventana no sabe de Revit: pide el 3D por
/// <see cref="VistaPrevia3DSolicitada"/> y <c>ComandoEstribos</c> lo hace.
/// </summary>
public partial class VentanaConfiguradorEstribos
{
    /// <summary>Se apretó «Ver en 3D» al menos una vez: desde ahí, cada cambio actualiza el 3D.</summary>
    private bool _vista3dActiva;

    /// <summary>
    /// La columna activa con todos sus tramos, los filtros, y si hay que abrir
    /// la vista 3D. Sin nadie escuchando (modo simulado, sin Revit) no hay 3D.
    /// </summary>
    public event Action<OpcionesArmado, FiltroPrevia3D, bool>? VistaPrevia3DSolicitada;

    /// <summary>Se llama una vez, desde <see cref="ConectarVistaPrevia"/>.</summary>
    private void ConectarVista3D()
    {
        PanelDe3D.VerSolicitado += () =>
        {
            _vista3dActiva = true;
            PedirVista3D(abrirVista: true);
        };
        PanelDe3D.FiltroCambiado += () => PedirVista3D(abrirVista: false);
    }

    /// <summary>Llamado por <c>ComandoEstribos</c> cuando la vista previa terminó en Revit (bien o con error).</summary>
    public void MostrarResumen3D(ResumenPrevia3D resumen)
    {
        // La simulación crea Rebar, regenera y revierte (y abrir la vista
        // también modifica el documento): la geometría que _contexto tenía
        // cacheada ya no es válida. Mismo motivo que en
        // MostrarResultadoGeneracion: sin esto, el próximo Aceptar tumbaba
        // Revit (docs/planes/2026-10-23-cierre-revit-vista-3d.md).
        _contexto.InvalidarCache();
        PanelDe3D.MostrarResumen(resumen);
    }

    private void PedirVista3D(bool abrirVista)
    {
        if (!_vista3dActiva) return;

        if (VistaPrevia3DSolicitada is null)
        {
            PanelDe3D.MostrarEstado("La vista 3D solo está disponible dentro de Revit.", aviso: true);
            return;
        }

        if (ConstruirPlanDeColumnaActiva() is not OpcionesArmado plan || _columnaActiva is null)
        {
            PanelDe3D.MostrarEstado("Elige una columna para verla en 3D.", aviso: true);
            return;
        }

        FiltroPrevia3D filtro = PanelDe3D.LeerFiltro();
        if (PanelDe3D.SoloTramoActivo)
        {
            Tramo tramo = _columnaActiva.Tramos[_tramoActivoIndice];
            filtro.SoloAltura = (tramo.ZInferior, tramo.ZSuperior);
        }

        PanelDe3D.MostrarEstado("Actualizando la vista 3D…");
        VistaPrevia3DSolicitada.Invoke(plan, filtro, abrirVista);
    }

    /// <summary>
    /// Como <see cref="ConstruirPlanArmado"/>, pero solo la columna activa y
    /// con TODOS sus tramos, aceptados o no: el del panel como está ahora, los
    /// demás con lo que tengan guardado o la configuración por defecto
    /// (decisión 5 del plan). No guarda nada: es solo para mirar.
    /// </summary>
    private OpcionesArmado? ConstruirPlanDeColumnaActiva()
    {
        if (_columnaActiva is null || _columnaIdActiva is not string id ||
            _tramoActivoIndice < 0 || _tramoActivoIndice >= _columnaActiva.Tramos.Count)
            return null;

        var tramos = new List<ConfigTramo?>();
        for (int i = 0; i < _columnaActiva.Tramos.Count; i++)
        {
            tramos.Add(i == _tramoActivoIndice
                ? LeerConfigDelPanel()
                : _configPorTramo.TryGetValue((id, i), out ConfigTramo? cfg) ? cfg : ConfigPorDefecto(_tipos));
        }

        return new OpcionesArmado
        {
            Plan = new PlanArmado
            {
                Columnas =
                {
                    new ColumnaPlan
                    {
                        Id = id, Contexto = _columnaActiva, Tramos = tramos, Longitudinal = LeerLongitudinalConEstribosMenores(),
                        Recubrimiento = _recubrimientoPorColumna.TryGetValue(id, out double recubrimiento) ? recubrimiento : null,
                    },
                },
            },
        };
    }
}
