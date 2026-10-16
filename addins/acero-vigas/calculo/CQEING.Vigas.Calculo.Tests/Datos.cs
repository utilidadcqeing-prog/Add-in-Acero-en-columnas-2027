using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>Datos de prueba compartidos.</summary>
internal static class Datos
{
    /// <summary>
    /// El pórtico de la maqueta del tablero: ejes A, B, C y D en 0, 4.40, 7.90 y 12.30 m, columnas de 0.40 m
    /// centradas en su eje y un voladizo de 1.50 m después de D (termina en 14.00).
    /// </summary>
    public static Portico PorticoMaqueta() => new(
        [Columna("A", 0), Columna("B", 4400), Columna("C", 7900), Columna("D", 12300)],
        VoladizoFinal: 1500);

    public static Apoyo Columna(string eje, int posicion, int ancho = 400) =>
        new(eje, posicion, posicion - ancho / 2, posicion + ancho / 2);
}
