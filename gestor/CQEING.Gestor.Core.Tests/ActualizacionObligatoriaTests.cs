using CQEING.Compartido;
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class ActualizacionObligatoriaTests : IDisposable
{
    private readonly CarpetaTemporal _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static string Dll(string id, string version, string anio) =>
        Path.Combine(@"C:\Users\u\AppData\Local\CQEING\Addins", id, version, anio, "addin.dll");

    [Fact]
    public void Reconoce_la_DLL_que_instalo_el_gestor()
    {
        Assert.Equal(new InstalacionAddin("acero-vigas", "1.0.1", "2026"), ActualizacionObligatoria.Instalacion(Dll("acero-vigas", "1.0.1", "2026")));
    }

    [Theory]
    [InlineData(@"C:\repo\addins\acero-vigas\bin\Debug\2026\CQEING.Vigas.dll")] // compilación de desarrollo
    [InlineData(@"C:\Users\u\AppData\Local\CQEING\Addins\acero-vigas\ultima\2026\addin.dll")] // versión sin formato
    [InlineData(@"C:\Users\u\AppData\Local\Otra\Addins\acero-vigas\1.0.0\2026\addin.dll")] // fuera de CQEING
    [InlineData(@"addin.dll")]
    public void Lo_que_no_instalo_el_gestor_nunca_se_bloquea(string dll)
    {
        Assert.Null(ActualizacionObligatoria.Instalacion(dll));
    }

    [Theory]
    [InlineData("1.0.0", "1.1.0", "1.1.0")]
    [InlineData("1.0.0", "1.0.0", null)]
    [InlineData("1.2.0", "1.1.0", null)] // instalada más nueva que la publicada: por ejemplo, una de prueba
    [InlineData("1.1.0-rc.1", "1.1.0", "1.1.0")] // la prerelease va antes que su estable
    [InlineData("1.1.0-rc.2", "1.0.9", null)]
    [InlineData("1.0.0", "basura", null)]
    public void Pendiente_solo_si_la_publicada_es_mas_nueva(string instalada, string publicada, string? esperada)
    {
        string carpeta = _tmp.Combinar("CQEING");
        ActualizacionObligatoria.Escribir(carpeta, "2026", new Dictionary<string, string> { ["acero-vigas"] = publicada });

        Assert.Equal(esperada, ActualizacionObligatoria.Pendiente(new InstalacionAddin("acero-vigas", instalada, "2026"), carpeta));
    }

    [Fact]
    public void Sin_saber_la_ultima_version_deja_usarlo()
    {
        string carpeta = _tmp.Combinar("CQEING");
        var vigas = new InstalacionAddin("acero-vigas", "1.0.0", "2026");
        Assert.Null(ActualizacionObligatoria.Pendiente(vigas, carpeta)); // sin archivo

        ActualizacionObligatoria.Escribir(carpeta, "2026", new Dictionary<string, string> { ["armado-columnas"] = "9.0.0" });
        Assert.Null(ActualizacionObligatoria.Pendiente(vigas, carpeta)); // no aparece en la lista

        File.WriteAllText(Path.Combine(carpeta, ActualizacionObligatoria.NombreArchivo("2026")), "{ dañado");
        Assert.Null(ActualizacionObligatoria.Pendiente(vigas, carpeta)); // archivo dañado
    }

    [Fact]
    public void Cada_anio_de_Revit_tiene_su_lista()
    {
        string carpeta = _tmp.Combinar("CQEING");
        ActualizacionObligatoria.Escribir(carpeta, "2027", new Dictionary<string, string> { ["acero-vigas"] = "2.0.0" });

        Assert.Null(ActualizacionObligatoria.Pendiente(new InstalacionAddin("acero-vigas", "1.0.0", "2026"), carpeta));
        Assert.Equal("2.0.0", ActualizacionObligatoria.Pendiente(new InstalacionAddin("acero-vigas", "1.0.0", "2027"), carpeta));
    }

    [Fact]
    public void Escribir_reemplaza_la_lista_anterior_sin_dejar_el_temporal()
    {
        string carpeta = _tmp.Combinar("CQEING");
        ActualizacionObligatoria.Escribir(carpeta, "2026", new Dictionary<string, string> { ["acero-vigas"] = "1.0.0" });
        ActualizacionObligatoria.Escribir(carpeta, "2026", new Dictionary<string, string> { ["acero-vigas"] = "1.1.0" });

        Assert.Equal("1.1.0", ActualizacionObligatoria.Leer(carpeta, "2026")["acero-vigas"]);
        Assert.Equal(new[] { ActualizacionObligatoria.NombreArchivo("2026") }, Directory.GetFiles(carpeta).Select(Path.GetFileName));
    }
}
