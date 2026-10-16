using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Nucleo;

namespace CQEING.Vigas.Calculo.Vigas;

/// <summary>Luz libre entre las caras de dos apoyos seguidos, en mm desde el eje A.</summary>
public sealed record Luz(string Nombre, int CaraIzquierda, int CaraDerecha);

/// <summary>
/// Lo que el motor necesita saber de un pórtico: dónde empiezan y terminan las barras, qué luces hay y dónde
/// puede ir el traslapo en cada una. Todo en mm desde el eje A.
/// </summary>
public sealed class GeometriaPortico
{
    /// <summary>El traslapo no entra al nudo: queda al menos a esta distancia de la cara del apoyo.</summary>
    public const int DistanciaACara = 50;

    public GeometriaPortico(Portico portico, int recubrimientoPuntas)
    {
        IReadOnlyList<Apoyo> apoyos = portico.Apoyos;
        Portico = portico;
        InicioBarras = apoyos[0].CaraIzquierda - portico.VoladizoInicio + recubrimientoPuntas;
        FinBarras = apoyos[^1].CaraDerecha + portico.VoladizoFinal - recubrimientoPuntas;
        Luces = apoyos.Zip(apoyos.Skip(1), (a, b) => new Luz($"{a.Eje}-{b.Eje}", a.CaraDerecha, b.CaraIzquierda)).ToList();
    }

    public Portico Portico { get; }

    /// <summary>Punta de la parte recta de las barras del lado izquierdo (sin gancho).</summary>
    public int InicioBarras { get; }

    /// <summary>Punta de la parte recta de las barras del lado derecho (sin gancho).</summary>
    public int FinBarras { get; }

    /// <summary>Luces entre apoyos, de izquierda a derecha. Los voladizos no son luces: ahí nunca se traslapa.</summary>
    public IReadOnlyList<Luz> Luces { get; }

    /// <summary>Dónde puede ir el traslapo en <paramref name="luz"/> y cuál es su punto ideal.</summary>
    public Ranura Ranura(Luz luz, Referencia referencia)
    {
        int desde = luz.CaraIzquierda + DistanciaACara;
        int hasta = luz.CaraDerecha - DistanciaACara;
        int ideal = referencia switch
        {
            Referencia.Inicio => desde,
            Referencia.Fin => hasta,
            _ => (luz.CaraIzquierda + luz.CaraDerecha) / 2,
        };
        return new Ranura(desde, hasta, ideal, referencia);
    }

    /// <summary>
    /// En la viga aérea la capa superior traslapa al centro de la luz y la inferior junto a la cara; en la de
    /// cimentación, al revés. La capa de la cara se mide desde el inicio o desde el fin según <paramref name="cara"/>.
    /// </summary>
    public static Referencia ReferenciaDe(TipoViga tipo, Posicion capa, Cara cara)
    {
        Posicion capaDelCentro = tipo == TipoViga.Aerea ? Posicion.Superior : Posicion.Inferior;
        if (capa == capaDelCentro)
            return Referencia.Centro;
        return cara == Cara.Inicio ? Referencia.Inicio : Referencia.Fin;
    }
}
