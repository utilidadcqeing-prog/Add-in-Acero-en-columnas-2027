namespace practicacq.Modelo;

/// <summary>
/// Un estribo menor tipo "caja": un rectángulo nested adentro del estribo
/// principal, que arma la ventana de estribos menores (L4) cruzando un RANGO
/// de filas tildadas (posición Y) con un RANGO de columnas tildadas (posición
/// X) contiguas entre sí -- confirmado por William en la reunión del 4-sep
/// (21:08-23:18): filas/columnas tildadas para caja que quedan pegadas
/// (índices consecutivos) se fusionan en un solo rectángulo grande ("toma el
/// área más grande"), no una caja por cada una. Un salto entre tildadas corta
/// la fusión y arma un rango separado. Ver
/// <c>VentanaEstribosMenores.Rangos</c> (quien arma esta lista) y
/// <c>GeneradorLongitudinal.GenerarMenores</c> (quien la geometriza).
///
/// El rectángulo final va literalmente de <see cref="ColumnaDesde"/> a
/// <see cref="ColumnaHasta"/> y de <see cref="FilaDesde"/> a
/// <see cref="FilaHasta"/> -- ya NO centrado/espejado respecto al centro de
/// la sección (esa era una interpretación sin confirmar de una versión
/// anterior, issue #30).
///
/// Reemplaza al viejo <c>EstriboMenor</c> con un <c>Tipo</c> compartido entre
/// grapa y caja: las grapas ya no viven acá, son
/// <see cref="ConfigLongitudinal.GrapasFila"/>/<see cref="ConfigLongitudinal.GrapasColumna"/>
/// -- una grapa cruza de borde a borde en una sola fila o columna entera, no
/// es una posición de grilla como la caja.
/// </summary>
public sealed class CajaMenor
{
    /// <summary>Índice de columna (posición X, 0-based) donde arranca el rango fusionado, inclusive.</summary>
    public int ColumnaDesde { get; set; }

    /// <summary>Índice de columna (posición X, 0-based) donde termina el rango fusionado, inclusive.</summary>
    public int ColumnaHasta { get; set; }

    /// <summary>Índice de fila (posición Y, 0-based) donde arranca el rango fusionado, inclusive.</summary>
    public int FilaDesde { get; set; }

    /// <summary>Índice de fila (posición Y, 0-based) donde termina el rango fusionado, inclusive.</summary>
    public int FilaHasta { get; set; }
}
