using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// El nivel de cada viga sale de la altura de su cara de arriba, no solo de su nivel de referencia de Revit (Juan y
/// Santiago, 3-nov): en el modelo del ingeniero cada piso tiene su cielo raso debajo, y las piezas del cielo raso
/// referenciadas al piso salían en él, sobrepuestas con la losa. Cotas del modelo, en mm.
/// </summary>
public class NivelPorAlturaTests
{
    private static readonly NivelLeido CieloRasoP2 = new("N+Cielo raso P2", 7260);
    private static readonly NivelLeido P3 = new("N+8.01 P3", 8410);
    private static readonly NivelLeido CieloRasoP3 = new("N+Cielo raso P3", 11110);
    private static readonly NivelLeido P4 = new("N+12.26 P4", 12260);
    private static readonly NivelLeido[] Niveles = [CieloRasoP2, P3, CieloRasoP3, P4];

    [Fact]
    public void LaPiezaDelCieloRasoReferenciadaAlPiso_VaAlCieloRaso() =>
        Assert.Equal(CieloRasoP2, NivelPorAltura.Elegir(Niveles, P3, 7200));

    [Fact]
    public void LaVigaDeLaLosaEnSuNivel_SeQueda() =>
        Assert.Equal(P3, NivelPorAltura.Elegir(Niveles, P3, 8410));

    [Fact]
    public void LaVigaDeLaLosaReferenciadaAlCieloRaso_VaAlPiso() =>
        Assert.Equal(P3, NivelPorAltura.Elegir(Niveles, CieloRasoP2, 8410));

    /// <summary>A unos centímetros de su nivel (acabado, desfase pequeño) sigue en él aunque otro esté cerca.</summary>
    [Fact]
    public void CercaDeSuNivel_SeQuedaEnEl() =>
        Assert.Equal(P4, NivelPorAltura.Elegir(Niveles, P4, 12180));

    /// <summary>Una viga aérea a media altura, lejos de todo nivel, conserva su referencia.</summary>
    [Fact]
    public void LejosDeTodoNivel_ConservaSuReferencia() =>
        Assert.Equal(P3, NivelPorAltura.Elegir(Niveles, P3, 9800));

    [Fact]
    public void SinReferencia_TomaElNivelCercanoONinguno()
    {
        Assert.Equal(CieloRasoP3, NivelPorAltura.Elegir(Niveles, null, 11000));
        Assert.Null(NivelPorAltura.Elegir(Niveles, null, 9800));
    }
}
