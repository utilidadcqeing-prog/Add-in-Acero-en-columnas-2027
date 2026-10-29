using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>Datos de prueba compartidos.</summary>
internal static class Datos
{
    /// <summary>
    /// Las reglas generales sin redondeo de ganchos: para las pruebas de la lógica de los ganchos (qué punta lo lleva, el
    /// doblez, los largos), que se armaron con el largo exacto. El redondeo lo prueba GanchosTests aparte.
    /// </summary>
    public static Reglas ReglasSinRedondeo { get; } = Reglas.Generales with { RedondeoGancho = 0 };

    /// <summary>
    /// Las reglas generales sin la cola E de los ganchos, así cada gancho suma su B entera, como cuando se armaron los
    /// escenarios de la maqueta (S1 3.00, S1–S2 0.90, I3–I4 1.30…). Desde el 26-oct el gancho respeta la cola del cuadro
    /// (<see cref="Reglas.LargoGancho"/>); las pruebas de avisos, cambios y arrastre siguen con sus escenarios, que prueban
    /// esa lógica y no el largo del gancho. Por lo mismo, sin redondeo de ganchos (29-oct): ese lo prueba GanchosTests.
    /// </summary>
    public static Reglas ReglasDeLaMaqueta { get; } = Reglas.Generales with
    {
        RedondeoGancho = 0,
        PorDiametro = Reglas.Generales.PorDiametro.ToDictionary(par => par.Key, par => par.Value with
        {
            Gancho90 = par.Value.Gancho90 with { E = null },
            Gancho180 = par.Value.Gancho180 with { E = null },
            Estribo135 = par.Value.Estribo135 with { E = null },
        }),
    };

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
