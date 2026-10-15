namespace CQEING.Gestor.Core.Tests;

public class EtiquetaReleaseTests
{
    [Theory]
    [InlineData("armado-columnas-v1.2.0", "armado-columnas", "1.2.0")]
    [InlineData("imagenes-v1.0.0-rc.1", "imagenes", "1.0.0-rc.1")]
    [InlineData("gestor-v0.1.0", "gestor", "0.1.0")]
    public void Separa_id_y_version(string texto, string id, string version)
    {
        var e = EtiquetaRelease.Interpretar(texto);
        Assert.Equal(id, e!.Id);
        Assert.Equal(version, e.Version.ToString());
        Assert.Equal(texto, e.ToString());
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("armado-columnas-1.2.0")]
    [InlineData("Armado-v1.0.0")]
    [InlineData("armado_columnas-v1.0.0")]
    [InlineData("armado-columnas-v1.0")]
    public void Rechaza_etiquetas_mal_formadas(string texto) => Assert.Null(EtiquetaRelease.Interpretar(texto));
}
