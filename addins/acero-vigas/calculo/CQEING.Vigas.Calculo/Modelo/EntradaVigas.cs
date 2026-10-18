namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>Todo lo que el motor necesita para calcular un pórtico.</summary>
/// <param name="CaraElegida">Dónde traslapa la capa de la cara; null = la de menos kg.</param>
/// <param name="RecubrimientoPuntas">Distancia de la punta de las barras a la cara exterior del pórtico, en mm.</param>
/// <param name="Cambios">Cambios manuales del usuario; se aplican sobre la solución automática.</param>
/// <param name="Bastones">Lo que se eligió en el paso 2 del tablero; null = los valores por defecto (#89).</param>
public sealed record EntradaVigas(
    Portico Portico,
    TipoViga Tipo,
    IReadOnlyList<GrupoBarras> Superior,
    IReadOnlyList<GrupoBarras> Inferior,
    Reglas Reglas,
    Cara? CaraElegida = null,
    int RecubrimientoPuntas = 50,
    CambiosManuales? Cambios = null,
    ArmadoBastones? Bastones = null);
