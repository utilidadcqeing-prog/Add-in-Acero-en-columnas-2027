using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>Un color por diámetro (decisión 9 del plan docs/planes/2026-10-19-ajustes-tablero-vigas-100.md).</summary>
public class ColoresDiametroTests
{
    [Fact]
    public void CadaDiametroDel3Al11TieneUnColorDistinto()
    {
        string[] colores = Enumerable.Range(3, 9).Select(ColoresDiametro.De).ToArray();

        Assert.Equal(9, colores.Distinct().Count());
        Assert.DoesNotContain(ColoresDiametro.SinColor, colores);
    }

    [Fact]
    public void ElDe5EsRojoYElDe4Naranja_ComoDijoCarlos()
    {
        Assert.Equal(("#D32F2F", "#E8710A"), (ColoresDiametro.De(5), ColoresDiametro.De(4)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(12)]
    public void UnDiametroFueraDeLaTablaSaleGris(int diametro)
    {
        Assert.Equal(ColoresDiametro.SinColor, ColoresDiametro.De(diametro));
    }
}
