namespace CQEING.Vigas.Calculo.Nucleo;

/// <summary>
/// Una barra de la línea. <paramref name="Desde"/> y <paramref name="Hasta"/> son las puntas de su parte
/// recta; <paramref name="Largo"/> es el largo total, ganchos incluidos. Todo en mm.
/// </summary>
public sealed record BarraLinea(int Desde, int Hasta, int Largo);

/// <summary>Un traslapo: dónde empieza y termina, en mm, y a cuánto quedó de su punto ideal.</summary>
public sealed record TraslapoLinea(int Desde, int Hasta, double Desvio)
{
    public int Largo => Hasta - Desde;
}

/// <summary>La mejor solución encontrada para una <see cref="ProblemaLinea"/>.</summary>
public sealed record SolucionLinea(
    IReadOnlyList<BarraLinea> Barras,
    IReadOnlyList<TraslapoLinea> Traslapos,
    int BarrasDe12)
{
    public int Acero => Barras.Sum(b => b.Largo);
    public double DesvioTotal => Traslapos.Sum(t => t.Desvio);
}
