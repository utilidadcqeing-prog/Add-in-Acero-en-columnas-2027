using System.IO.Compression;
using CQEING.Gestor.Core;

namespace CQEING.Publicador.Tests;

public class PublicacionTests : IDisposable
{
    private static readonly DateTimeOffset Fecha = new(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);
    private readonly CarpetaTemporal _tmp = new();
    private readonly CompiladorFalso _compilador = new();

    public PublicacionTests()
    {
        string carpeta = _tmp.Combinar("repo", "addins", "armado-columnas");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "addin.json"), """
            {
              "id": "armado-columnas",
              "nombre": "Armado de columnas",
              "descripcion": "Estribos, columnetas y viguetas de confinamiento.",
              "proyecto": "practicacq.csproj",
              "revit": ["2026", "2027"],
              "heredados": ["practicacq.addin"]
            }
            """);
    }

    public void Dispose() => _tmp.Dispose();

    private Manifiesto Publicar(string etiqueta) =>
        new Publicacion(_compilador).Ejecutar(_tmp.Combinar("repo"), etiqueta, "Notas de la versión\n", Fecha, _tmp.Combinar("dist"));

    [Fact]
    public void Crea_un_zip_por_anio_y_el_manifiesto()
    {
        Manifiesto devuelto = Publicar("armado-columnas-v1.2.0");

        Manifiesto m = SerializadorManifiesto.Leer(File.ReadAllText(_tmp.Combinar("dist", "manifest.json")));
        Assert.Equal(devuelto.Paquetes, m.Paquetes);
        Assert.Equal(new[] { "2026", "2027" }, _compilador.Anios);
        Assert.Equal(("armado-columnas", "Armado de columnas", "1.2.0", "Notas de la versión", Fecha),
            (m.Id, m.Nombre, m.Version, m.Notas, m.PublicadoEn));
        Assert.Equal(new[] { "practicacq.addin" }, m.Heredados);
        Assert.Equal(
            new[] { ("2026", "net8.0-windows", "armado-columnas-1.2.0-revit2026.zip"), ("2027", "net10.0-windows", "armado-columnas-1.2.0-revit2027.zip") },
            m.Paquetes.Select(p => (p.Revit, p.Framework, p.Archivo)));
        foreach (PaqueteManifiesto p in m.Paquetes)
            Assert.Equal(Sha256Archivo.Calcular(_tmp.Combinar("dist", p.Archivo)), p.Sha256);
    }

    [Fact]
    public void Cada_zip_trae_la_compilacion_de_su_anio()
    {
        Publicar("armado-columnas-v1.2.0");

        using ZipArchive zip = ZipFile.OpenRead(_tmp.Combinar("dist", "armado-columnas-1.2.0-revit2027.zip"));
        using var lector = new StreamReader(zip.GetEntry("practicacq.dll")!.Open());
        Assert.Equal("dll 2027 1.2.0", lector.ReadToEnd());
    }

    [Fact]
    public void Prerelease_conserva_el_sufijo_en_la_version()
    {
        Assert.Equal("1.2.0-rc.1", Publicar("armado-columnas-v1.2.0-rc.1").Version);
        Assert.Equal("1.2.0-rc.1", _compilador.Versiones[0]);
    }

    [Theory]
    [InlineData("v1.2.0")]
    [InlineData("inexistente-v1.0.0")]
    public void Etiqueta_mal_formada_o_de_otro_addin_falla(string etiqueta) =>
        Assert.Throws<InvalidOperationException>(() => Publicar(etiqueta));

    private sealed class CompiladorFalso : ICompilador
    {
        public List<string> Anios { get; } = [];
        public List<string> Versiones { get; } = [];

        public string Compilar(string rutaProyecto, string anio, string version)
        {
            string salida = Path.Combine(Path.GetDirectoryName(rutaProyecto)!, "bin", "Release", anio);
            Directory.CreateDirectory(Path.Combine(salida, "Resources"));
            File.WriteAllText(Path.Combine(salida, "practicacq.dll"), $"dll {anio} {version}");
            File.WriteAllText(Path.Combine(salida, "practicacq.pdb"), "símbolos");
            File.WriteAllText(Path.Combine(salida, "practicacq.addin"), "<RevitAddIns/>");
            File.WriteAllText(Path.Combine(salida, "Resources", "icono16.png"), "png");
            File.WriteAllText(Path.Combine(salida, Empaquetador.ArchivoFramework), anio == "2026" ? "net8.0-windows\r\n" : "net10.0-windows\r\n");
            Anios.Add(anio);
            Versiones.Add(version);
            return salida;
        }
    }
}
