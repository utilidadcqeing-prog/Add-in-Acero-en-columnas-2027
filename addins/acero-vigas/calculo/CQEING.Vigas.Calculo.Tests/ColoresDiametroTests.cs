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
    public void ElDe5EsRojoYElDe4Azul()
    {
        Assert.Equal(("#EA4335", "#4285F4"), (ColoresDiametro.De(5), ColoresDiametro.De(4)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(12)]
    public void UnDiametroFueraDeLaTablaSaleGris(int diametro)
    {
        Assert.Equal(ColoresDiametro.SinColor, ColoresDiametro.De(diametro));
    }

    [Fact]
    public void RgbDaLosTresCanalesDelColor_ParaLosFiltrosDeRevit()
    {
        Assert.Equal(((byte)0xEA, (byte)0x43, (byte)0x35), ColoresDiametro.Rgb(5));
        Assert.Equal(((byte)0x5E, (byte)0x66, (byte)0x70), ColoresDiametro.Rgb(99));
    }

    /// <summary>Casi negro y no negro puro: con el fondo oscuro, Revit pinta de blanco lo que es (0, 0, 0).</summary>
    [Fact]
    public void ElContornoDeTodaBarraEsCasiNegro()
    {
        Assert.Equal(((byte)30, (byte)30, (byte)30), ColoresDiametro.RgbContorno);
    }
}
