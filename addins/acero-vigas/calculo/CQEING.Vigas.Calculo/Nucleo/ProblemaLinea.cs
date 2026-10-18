namespace CQEING.Vigas.Calculo.Nucleo;

/// <summary>Qué parte del traslapo se compara con el punto ideal de su ranura.</summary>
public enum Referencia
{
    /// <summary>El inicio del traslapo (capa de la cara, traslapo al inicio de la luz).</summary>
    Inicio,
    /// <summary>El centro del traslapo (capa del centro).</summary>
    Centro,
    /// <summary>El fin del traslapo (capa de la cara, traslapo al final de la luz).</summary>
    Fin,
}

/// <summary>
/// Lugar donde puede ir un traslapo. El traslapo entero tiene que quedar entre
/// <paramref name="ZonaDesde"/> y <paramref name="ZonaHasta"/>, y se busca que su
/// <paramref name="Referencia"/> quede lo más cerca posible de <paramref name="PuntoIdeal"/>, sin alejarse más de
/// <paramref name="DesvioMaximo"/>. Todo en mm.
/// </summary>
public sealed record Ranura(int ZonaDesde, int ZonaHasta, int PuntoIdeal, Referencia Referencia, int DesvioMaximo = int.MaxValue);

/// <summary>
/// Una línea de barras entre dos puntas fijas (una capa de viga o la longitudinal de una columna), en mm.
/// </summary>
/// <param name="Inicio">Donde termina la parte recta de la primera barra.</param>
/// <param name="Fin">Donde termina la parte recta de la última barra.</param>
/// <param name="GanchoInicio">Largo del gancho de la primera barra (0 si no lleva).</param>
/// <param name="GanchoFin">Largo del gancho de la última barra (0 si no lleva).</param>
/// <param name="Ranuras">Una por traslapo, en orden; con N ranuras salen N + 1 barras.</param>
/// <param name="TraslapoMinimo">Ningún traslapo puede ser más corto.</param>
/// <param name="Modulo">Múltiplo de corte de las barras (500 = 0.50 m).</param>
/// <param name="VarillaBase">Varilla comercial normal; una barra más larga cuenta como "de 12 m".</param>
/// <param name="VarillaMaxima">Ninguna barra puede pasar de este largo.</param>
/// <param name="LargosFijos">Cambios manuales: índice de barra (desde 0) → largo total.</param>
public sealed record ProblemaLinea(
    int Inicio,
    int Fin,
    int GanchoInicio,
    int GanchoFin,
    IReadOnlyList<Ranura> Ranuras,
    int TraslapoMinimo,
    int Modulo,
    int VarillaBase,
    int VarillaMaxima,
    IReadOnlyDictionary<int, int>? LargosFijos = null);
