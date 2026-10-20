namespace practicacq.Modelo;

/// <summary>
/// Dónde está una barra longitudinal en la sección, que es lo que decide su
/// número de barra (<see cref="ConfigLongitudinal.BarraDe"/>).
/// </summary>
public enum PosicionBarra
{
    Esquina,

    /// <summary>Intermedia de la cara inferior o superior (paralela al eje X).</summary>
    CaraX,

    /// <summary>Intermedia de la cara izquierda o derecha (paralela al eje Y).</summary>
    CaraY,
}
