namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using practicacq.Modelo;

/// <summary>
/// La vista previa 3D (#123) en contexto de API válido, por ExternalEvent
/// (la ventana es modeless): simula la columna (<see cref="SimuladorArmado"/>),
/// la clasifica (<see cref="ClasificadorPrevia3D"/>), aplica los filtros y la
/// pinta (<see cref="Tubos3D"/>). Si solo cambiaron los filtros, reutiliza lo
/// ya simulado: filtrar no vuelve a generar
/// (docs/planes/2026-10-21-vista-previa-3d-columnas-123.md, paso 5).
///
/// Los pedidos que llegan seguidos se juntan solos: Revit atiende un
/// <c>Raise</c> pendiente una sola vez, y en ese momento se usa el último
/// pedido que dejó la ventana.
/// </summary>
public class ManejadorVistaPrevia3D : IExternalEventHandler
{
    /// <summary>El documento que lee la ventana: con otro activo no se simula nada.</summary>
    public Document? Documento { get; set; }

    /// <summary>La columna activa, con todos sus tramos (decisión 5).</summary>
    public OpcionesArmado? Plan { get; set; }

    public FiltroPrevia3D Filtro { get; set; } = new();

    /// <summary>Abrir (o crear) la vista 3D en el próximo pedido; se apaga solo después.</summary>
    public bool AbrirVista { get; set; }

    public Action<ResumenPrevia3D>? AlTerminar { get; set; }

    private string? _claveSimulada;
    private Document? _documentoSimulado;
    private List<BarraPrevia> _barras = new();
    private TimeSpan _tiempo;
    private int _avisos;

    public void Execute(UIApplication app)
    {
        try
        {
            UIDocument? uidoc = app.ActiveUIDocument;
            if (uidoc?.Document is not Document doc || (Documento is not null && !doc.Equals(Documento)))
            {
                AlTerminar?.Invoke(new ResumenPrevia3D("Activa en Revit el modelo de la ventana para ver la vista 3D.", Array.Empty<int>(), Fallo: true));
                return;
            }

            if (Plan?.Plan.Columnas.FirstOrDefault() is not ColumnaPlan columna || columna.Contexto is null) return;

            ConfigLongitudinal? l = columna.Longitudinal;
            Bitacora3D.Anotar($"3D pedido: columna {columna.Contexto.Marca}, abrir={AbrirVista}, tramos={columna.Tramos.Count}, " +
                              $"menores={l?.EstribosMenores?.Count ?? -1} (para generar {l?.MenoresParaGenerar().Count ?? -1}), " +
                              $"grapasFila={l?.GrapasFila?.Count ?? -1}, grapasColumna={l?.GrapasColumna?.Count ?? -1}, cajas={l?.Cajas?.Count ?? -1}");

            if (AbrirVista)
            {
                AbrirVista = false;
                Bitacora3D.Anotar("abrir vista: empieza");
                Vista3DColumna.Abrir(uidoc, columna.Contexto);
                Bitacora3D.Anotar("abrir vista: listo");
            }

            // La fecha del plan cambia en cada pedido: la clave es solo la columna.
            string clave = JsonSerializer.Serialize(Plan.Plan.Columnas);
            if (clave != _claveSimulada || !doc.Equals(_documentoSimulado))
            {
                SimuladorArmado.Resultado simulacion = SimuladorArmado.Simular(doc, Plan);
                _barras = ClasificadorPrevia3D.Clasificar(simulacion, columna);
                Bitacora3D.Anotar("clasificadas: " + string.Join(", ", _barras.GroupBy(b => b.Clase).Select(g => $"{g.Key} {g.Count()}")) +
                                  $"; avisos: {string.Join(" | ", simulacion.Generacion.Avisos)}");
                _tiempo = simulacion.Tiempo;
                _avisos = simulacion.Generacion.Avisos.Count;
                _claveSimulada = clave;
                _documentoSimulado = doc;
            }

            List<Tubos3D.Tubo> tubos = Filtro.Aplicar(_barras).Select(Tubo).ToList();
            Bitacora3D.Anotar($"mostrar {tubos.Count} tubos");
            Tubos3D.Mostrar(doc, tubos);
            uidoc.RefreshActiveView();
            Bitacora3D.Anotar("refrescado");

            List<int> numeros = _barras.Select(b => FiltroPrevia3D.NumeroDe(b.DiametroMm)).Distinct().OrderBy(n => n).ToList();
            string texto = $"{_barras.Count} barras ({_tiempo.TotalSeconds:0.0} s)";
            if (tubos.Count == 0 && _barras.Count > 0) texto += " · los filtros no dejan ver ninguna";
            if (_avisos > 0) texto += $" · {_avisos} aviso(s) del generador: mira «Generar» para el detalle";
            AlTerminar?.Invoke(new ResumenPrevia3D(texto, numeros));
        }
        catch (Exception ex)
        {
            // Un fallo de la vista previa no puede tumbar Revit ni la ventana.
            Bitacora3D.Anotar($"EXCEPCIÓN: {ex}");
            AlTerminar?.Invoke(new ResumenPrevia3D($"No se pudo armar la vista 3D: {GeometriaSeccion.Explicar(ex)}", Array.Empty<int>(), Fallo: true));
        }
    }

    /// <summary>Quita el dibujo y olvida lo simulado (al cerrar la ventana o el documento).</summary>
    public void Olvidar()
    {
        Tubos3D.Quitar();
        _claveSimulada = null;
        _documentoSimulado = null;
        _barras = new List<BarraPrevia>();
    }

    private static Tubos3D.Tubo Tubo((IReadOnlyList<Punto3> Puntos, double DiametroMm) pedazo)
    {
        ColoresVarilla.ColorRgb c = ColoresVarilla.ColorDe(pedazo.DiametroMm);
        return new Tubos3D.Tubo(
            pedazo.Puntos.Select(p => new XYZ(EstandarCQ.APies(p.X), EstandarCQ.APies(p.Y), EstandarCQ.APies(p.Z))).ToList(),
            EstandarCQ.APies(pedazo.DiametroMm / 2000),
            new ColorWithTransparency(c.R, c.G, c.B, 0));
    }

    public string GetName() => "Vista previa 3D del armado de columnas";
}
