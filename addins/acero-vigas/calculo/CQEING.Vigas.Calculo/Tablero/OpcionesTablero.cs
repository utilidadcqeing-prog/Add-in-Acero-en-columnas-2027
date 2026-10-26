using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>
/// Lo que el usuario elige en los pasos 1 a 3 del tablero (#83) para un pórtico: el <c>state</c> de la maqueta que
/// cambia el cálculo. Los recubrimientos y la separación de filas van en cm, como se escriben en el paso 1;
/// <see cref="Bastones"/> y <see cref="Estribos"/> van en mm, como en el motor.
/// </summary>
/// <param name="Tipo">Aérea o cimentación; null = la que se leyó del modelo.</param>
/// <param name="RecArribaCm">Recubrimiento de una cara en "Más opciones"; null = <paramref name="RecubrimientoCm"/>.</param>
/// <param name="SeparacionFilasCm">Separación libre entre filas; null = filas pegadas.</param>
/// <param name="Cara">Dónde traslapa la capa de la cara; null = la más económica.</param>
/// <param name="EstribosMarcados">Los estribos de los tramos marcados en la lista; null = los de todo el pórtico.</param>
/// <param name="Corte">El corte 1-1 del paso 4 (#91), en mm; null = la vista tipo con la profundidad de base.</param>
/// <param name="Elemento">Viga o vigueta corregida a mano con la casilla "Es vigueta" (#940); null = la que se detectó.</param>
public sealed record OpcionesTablero(
    TipoViga? Tipo,
    IReadOnlyList<GrupoBarras> Superior,
    IReadOnlyList<GrupoBarras> Inferior,
    double RecubrimientoCm,
    double? RecArribaCm,
    double? RecAbajoCm,
    double? RecLadosCm,
    double? SeparacionFilasCm,
    int MaximoFilas,
    Cara? Cara,
    ArmadoBastones Bastones,
    ConfigEstribos Estribos,
    IReadOnlyList<string> TramosMarcados,
    ConfigEstribos? EstribosMarcados,
    CambiosManuales? Cambios,
    OpcionesCorte? Corte = null,
    Elemento? Elemento = null)
{
    /// <summary>El <c>state</c> inicial de la maqueta: 3 #5 arriba y abajo, recubrimiento 4, filas pegadas, 3 filas.</summary>
    public static OpcionesTablero PorDefecto { get; } = new(
        null, [new GrupoBarras(3, 5)], [new GrupoBarras(3, 5)], 4, null, null, null, null, 3, null,
        new ArmadoBastones(), new ConfigEstribos(), [], null, null);

    /// <summary>
    /// Una vigueta de 10 cm (#942) arranca con 1 #4 arriba y abajo y recubrimiento 3, como la Vigueta T1 del plano de
    /// referencia (decisión 3 del plan docs/planes/2026-10-26-acero-viguetas-938.md): con las de una viga no cabe ninguna
    /// barra.
    /// </summary>
    public static OpcionesTablero DeVigueta { get; } = PorDefecto with
    {
        Superior = [new GrupoBarras(1, 4)], Inferior = [new GrupoBarras(1, 4)], RecubrimientoCm = 3,
    };

    /// <summary>Con las que arranca un pórtico que el usuario todavía no tocó.</summary>
    public static OpcionesTablero Para(Elemento elemento) => elemento == Modelo.Elemento.Vigueta ? DeVigueta : PorDefecto;
}
