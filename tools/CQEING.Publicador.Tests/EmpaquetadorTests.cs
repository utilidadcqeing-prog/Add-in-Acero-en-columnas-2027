using System.IO.Compression;

namespace CQEING.Publicador.Tests;

public class EmpaquetadorTests : IDisposable
{
    private readonly CarpetaTemporal _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private string Compilada(int addins)
    {
        string carpeta = _tmp.Combinar("salida");
        Directory.CreateDirectory(Path.Combine(carpeta, "Resources"));
        File.WriteAllText(Path.Combine(carpeta, "practicacq.dll"), "dll");
        File.WriteAllText(Path.Combine(carpeta, "practicacq.pdb"), "símbolos");
        File.WriteAllText(Path.Combine(carpeta, Empaquetador.ArchivoFramework), "net8.0-windows");
        File.WriteAllText(Path.Combine(carpeta, "Resources", "icono16.png"), "png");
        for (int i = 0; i < addins; i++) File.WriteAllText(Path.Combine(carpeta, $"a{i}.addin"), "<RevitAddIns/>");
        return carpeta;
    }

    [Fact]
    public void Zip_sin_pdb_ni_framework_y_con_resources()
    {
        string zip = _tmp.Combinar("p.zip");
        Empaquetador.CrearZip(Compilada(addins: 1), zip);

        using ZipArchive archivo = ZipFile.OpenRead(zip);
        Assert.Equal(new[] { "Resources/icono16.png", "a0.addin", "practicacq.dll" }, archivo.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Exige_exactamente_un_addin(int addins) =>
        Assert.Throws<InvalidOperationException>(() => Empaquetador.CrearZip(Compilada(addins), _tmp.Combinar("p.zip")));
}
