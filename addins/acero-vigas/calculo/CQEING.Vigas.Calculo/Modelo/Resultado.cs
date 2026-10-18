using CQEING.Vigas.Calculo.Nucleo;

namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>
/// Una barra corrida, tal como la pinta el tablero. <paramref name="Desde"/> y <paramref name="Hasta"/> son las
/// puntas de su parte recta, en mm desde el eje A; <paramref name="Largo"/> incluye los ganchos.
/// </summary>
public sealed record Barra(
    string Id,
    Posicion Capa,
    int Desde,
    int Hasta,
    int GanchoInicio,
    int GanchoFin,
    int Largo,
    int Varilla,
    bool Manual,
    bool LlegaAlVoladizo,
    IReadOnlyList<GrupoBarras> Grupos,
    double Kg)
{
    public int LargoRecto => Hasta - Desde;
}

/// <summary>Un traslapo entre dos barras de una capa (<c>S1–S2</c>), en mm desde el eje A.</summary>
public sealed record Traslapo(
    string Id,
    string BarraIzquierda,
    string BarraDerecha,
    Posicion Capa,
    string Luz,
    int Desde,
    int Hasta,
    int Minimo,
    int Diametro,
    double Desvio,
    bool Manual)
{
    public int Largo => Hasta - Desde;
}

/// <summary>Las barras y traslapos de una capa, con el criterio que se usó para ubicar sus traslapos.</summary>
public sealed record SolucionCapa(Posicion Capa, Referencia Referencia, IReadOnlyList<Barra> Barras, IReadOnlyList<Traslapo> Traslapos)
{
    public double Kg => Barras.Sum(b => b.Kg);
}

public enum TipoAviso
{
    /// <summary>Un diámetro del armado no tiene traslapo mínimo en las reglas. No se calcula nada.</summary>
    SinTraslapoMinimo,
    /// <summary>Una capa no tiene ninguna solución válida, ni con varilla de 12 m.</summary>
    SinSolucion,
    /// <summary>Un traslapo supera el mínimo en más de 0.30 m.</summary>
    TraslapoLargo,
    /// <summary>Una barra no sale de la varilla normal y usa la larga (12 m).</summary>
    VarillaLarga,
    /// <summary>Un cambio manual guardado ya no se puede aplicar; se ignora (ver <see cref="Aviso.Motivo"/>).</summary>
    CambioNoAplicado,
    /// <summary>El diámetro de los bastones de una cara no está en las reglas; esa cara queda sin bastones.</summary>
    DiametroSinReglas,
    /// <summary>El largo general de los bastones es 0 o negativo; el pórtico queda sin bastones.</summary>
    BastonLargoNoValido,
    /// <summary>
    /// Una separación de los estribos es 0 o negativa, o el largo de la zona es negativo; ese tramo (o el pórtico, con
    /// <c>"general"</c>) queda sin estribos.
    /// </summary>
    EstriboValorNoValido,
    /// <summary>
    /// Un estribo adicional no tiene las barras que pide: el cerrado, 2 arriba y 2 abajo; el abierto, 1 y 1. No se
    /// agrega. <see cref="Aviso.Elemento"/> es su id (<c>"E1"</c>), como en los avisos que siguen.
    /// </summary>
    EstriboIncompleto,
    /// <summary>Un estribo adicional nombra una fila o un número de barra que no hay en el corte. No se agrega.</summary>
    BarraNoExiste,
    /// <summary>Alinear el adicional movería una barra de esquina, que abraza el cerrado obligatorio. No se agrega.</summary>
    MueveUnaEsquina,
    /// <summary>
    /// Ni corriendo las barras vecinas queda la distancia libre (o la barra ya la alineó otro adicional, o la sección
    /// tiene un error). No se agrega.
    /// </summary>
    NoSePuedeAlinear,
    /// <summary>El adicional abraza las mismas barras que el cerrado de las esquinas (o una de sus ramas). No se agrega.</summary>
    IgualAlCerrado,
}

/// <summary>Por qué un traslapo quedó largo.</summary>
public enum CausaAviso
{
    Ninguna,
    /// <summary>Toca la primera o la última barra, que tiene una punta fija y solo puede crecer.</summary>
    VecinaDeFrontera,
}

/// <summary>Qué pasaría con la otra opción de la capa de la cara. <paramref name="KgAhorro"/> &lt; 0 si cuesta más.</summary>
public sealed record AlternativaCara(Cara Cara, bool LoEvita, double KgAhorro);

/// <summary>
/// Un aviso con sus datos; el texto lo arma el tablero. <paramref name="Elemento"/> es el id del traslapo o de la
/// barra (o el diámetro, como "#2"); <paramref name="Largo"/> y <paramref name="Exceso"/> van en mm.
/// </summary>
public sealed record Aviso(
    TipoAviso Tipo,
    string Elemento,
    int Largo = 0,
    int Exceso = 0,
    CausaAviso Causa = CausaAviso.Ninguna,
    AlternativaCara? Alternativa = null,
    MotivoBloqueo Motivo = MotivoBloqueo.Ninguno);

/// <summary>
/// Lo que devuelve el motor. <paramref name="Superior"/> e <paramref name="Inferior"/> son las capas en uso (la de
/// la cara, con <paramref name="CaraUsada"/>). <paramref name="CaraInicio"/> y <paramref name="CaraFinal"/> son las
/// dos opciones completas de la capa de la cara, para la tarjeta "¿Dónde traslapan…?". <paramref name="Kg"/> suma las
/// barras corridas y los bastones.
/// </summary>
public sealed record ResultadoVigas(
    SolucionCapa? Superior,
    SolucionCapa? Inferior,
    Posicion CapaDeLaCara,
    SolucionCapa? CaraInicio,
    SolucionCapa? CaraFinal,
    Cara CaraUsada,
    Cara CaraMasEconomica,
    IReadOnlyList<Aviso> Avisos,
    IReadOnlyList<Baston> Bastones)
{
    public IEnumerable<Barra> Barras => Capas.SelectMany(c => c.Barras);
    public IEnumerable<Traslapo> Traslapos => Capas.SelectMany(c => c.Traslapos);
    public double Kg => Capas.Sum(c => c.Kg) + Bastones.Sum(b => b.Kg);

    /// <summary>Tramos con traslapo (de las dos capas en uso), donde el corte 1-1 no se puede ubicar (#91).</summary>
    public IReadOnlyList<(int Desde, int Hasta)> ZonasTraslapo => Traslapos.Select(t => (t.Desde, t.Hasta)).ToList();

    private IEnumerable<SolucionCapa> Capas => new[] { Superior, Inferior }.OfType<SolucionCapa>();
}
