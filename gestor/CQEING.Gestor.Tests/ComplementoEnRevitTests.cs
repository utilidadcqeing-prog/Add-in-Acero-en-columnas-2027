using CQEING.Gestor.Core;

namespace CQEING.Gestor.Tests;

public class ComplementoEnRevitTests
{
    [Fact]
    public void La_version_del_gestor_sale_sin_el_commit_y_es_valida_para_las_carpetas()
    {
        string version = ComplementoEnRevit.VersionDelGestor(typeof(ComplementoEnRevit).Assembly);

        Assert.DoesNotContain('+', version);
        Assert.NotNull(VersionAddin.Interpretar(version));
    }

    [Fact]
    public void La_segunda_ventana_se_detecta_mientras_la_primera_existe()
    {
        string nombre = @"Local\CQEING.Gestor.Pruebas." + Guid.NewGuid().ToString("N");
        using var primera = new UnaSolaVentana(nombre);
        using var segunda = new UnaSolaVentana(nombre);

        Assert.True(primera.EsLaPrimera);
        Assert.False(segunda.EsLaPrimera);
    }

    [Fact]
    public void Al_cerrar_la_primera_otra_puede_ser_la_primera()
    {
        string nombre = @"Local\CQEING.Gestor.Pruebas." + Guid.NewGuid().ToString("N");
        using (var primera = new UnaSolaVentana(nombre))
            Assert.True(primera.EsLaPrimera);

        using var siguiente = new UnaSolaVentana(nombre);
        Assert.True(siguiente.EsLaPrimera);
    }
}
