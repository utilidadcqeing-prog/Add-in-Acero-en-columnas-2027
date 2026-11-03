namespace CQEING.Vigas.Calculo.Porticos;

/// <summary>
/// El nivel donde va una viga, por la altura de su cara de arriba (Juan y Santiago, 3-nov). En el modelo del ingeniero
/// cada piso tiene su cielo raso entre 0.75 y 1.15 m debajo, y las piezas del cielo raso referenciadas al piso salían en
/// la planta del piso, sobrepuestas con la losa. Así cada piso y cada cielo raso quedan separados, sin depender de la
/// referencia con que se modeló cada viga.
/// </summary>
public static class NivelPorAltura
{
    /// <summary>Hasta aquí del nivel de referencia, la viga sigue en él (acabado, desfases pequeños), en mm.</summary>
    private const double ToleranciaPropio = 100;

    /// <summary>
    /// Hasta aquí de otro nivel, la viga va a ese, en mm. Menos que la distancia del cielo raso al piso (750 mm) para no
    /// confundirlos; más allá la viga es aérea y conserva su referencia.
    /// </summary>
    private const double ToleranciaOtro = 300;

    /// <summary>
    /// <paramref name="referencia"/> si la cara de arriba está a su altura; si no, el nivel más cercano a ella, si está a
    /// menos de <see cref="ToleranciaOtro"/>; si no hay ninguno, la referencia (null si no tiene).
    /// </summary>
    public static NivelLeido? Elegir(IReadOnlyList<NivelLeido> niveles, NivelLeido? referencia, double zArriba)
    {
        if (referencia is not null && Math.Abs(referencia.Elevacion - zArriba) <= ToleranciaPropio)
            return referencia;
        NivelLeido? cercano = niveles.OrderBy(n => Math.Abs(n.Elevacion - zArriba)).FirstOrDefault();
        return cercano is not null && Math.Abs(cercano.Elevacion - zArriba) <= ToleranciaOtro ? cercano : referencia;
    }
}
