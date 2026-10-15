namespace CQEING.Gestor.Core.Tests;

public class VersionAddinTests
{
    [Fact]
    public void Interpreta_version_estable()
    {
        var v = VersionAddin.Interpretar("1.2.3");
        Assert.Equal(new VersionAddin(1, 2, 3, null), v);
        Assert.True(v!.EsEstable);
        Assert.Equal("1.2.3", v.ToString());
    }

    [Fact]
    public void Interpreta_prerelease()
    {
        var v = VersionAddin.Interpretar("1.2.0-rc.4");
        Assert.Equal(new VersionAddin(1, 2, 0, 4), v);
        Assert.False(v!.EsEstable);
        Assert.Equal("1.2.0-rc.4", v.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.2")]
    [InlineData("v1.2.0")]
    [InlineData("1.2.0-beta")]
    [InlineData("1.2.0 ")]
    [InlineData("12345678901.0.0")]
    public void Rechaza_textos_que_no_son_version(string? texto) => Assert.Null(VersionAddin.Interpretar(texto));

    [Theory]
    [InlineData("1.10.0", "1.9.0")]
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("1.2.0", "1.2.0-rc.5")]
    [InlineData("1.2.0-rc.2", "1.2.0-rc.1")]
    public void Ordena_por_numero_no_por_texto(string mayor, string menor) =>
        Assert.True(VersionAddin.Interpretar(mayor)!.CompareTo(VersionAddin.Interpretar(menor)) > 0);
}
