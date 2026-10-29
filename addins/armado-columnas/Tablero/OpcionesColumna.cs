namespace practicacq.Tablero;

using System.Linq;
using practicacq.Modelo;

/// <summary>
/// Lo que el usuario eligió para una columna en la interfaz web: el acero
/// longitudinal (con sus grapas y estribos cerrados), los estribos de toda la
/// columna y el recubrimiento.
/// </summary>
public sealed class OpcionesColumna
{
    public ConfigLongitudinal Longitudinal { get; set; } = new();

    public ConfigEstribosColumna Estribos { get; set; } = new();

    /// <summary>null = el que trae la columna en Revit (<see cref="ColumnaContexto.Recubrimiento"/>).</summary>
    public double? Recubrimiento { get; set; }

    public OpcionesColumna Copiar() => new()
    {
        Longitudinal = Longitudinal.Copiar(),
        Estribos = Estribos.Copiar(),
        Recubrimiento = Recubrimiento,
    };

    public double RecubrimientoDe(ColumnaContexto columna) => Recubrimiento ?? columna.Recubrimiento;

    /// <summary>Como las edita la página: barras de cada cara y ángulos en grados.</summary>
    public OpcionesVista AVista(ColumnaContexto? columna) => new(
        columna is null ? Recubrimiento ?? 0 : RecubrimientoDe(columna),
        new LongitudinalVista(
            Longitudinal.BarrasPorCaraX / 2, Longitudinal.BarrasPorCaraY / 2,
            Longitudinal.TipoBarraEsquinasNombre, Longitudinal.TipoBarraCaraXNombre, Longitudinal.TipoBarraCaraYNombre,
            Longitudinal.LongitudComercialBarra, Longitudinal.RedondeoVarillas, Longitudinal.SolapeMayorDeLosDos,
            Longitudinal.EstribosMenores
                .Select(p => new PiezaVista(p.Forma, p.Eje, p.Desde, p.Hasta, p.Volteado, (int)p.AnguloInicio, (int)p.AnguloFin, p.TipoBarraNombre))
                .ToList()),
        Estribos.Copiar());
}
