using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class LimpiezaVersionesViejasTests : IDisposable
{
    private readonly CarpetaTemporal _tmp = new();
    private readonly RutasGestor _rutas;

    public LimpiezaVersionesViejasTests() => _rutas = new RutasGestor(_tmp.Combinar("local"), _tmp.Combinar("roaming"));

    public void Dispose() => _tmp.Dispose();

    /// <summary>Descarga la versión en su carpeta y, si <paramref name="activa"/>, deja el .addin apuntándola.</summary>
    private void Instalar(string id, string version, string anio, bool activa = true)
    {
        string carpeta = _rutas.CarpetaVersion(id, version, anio);
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "addin.dll"), "");
        if (!activa) return;
        Directory.CreateDirectory(_rutas.CarpetaAddinsRevit(anio));
        File.WriteAllText(_rutas.ArchivoAddin(id, anio), Datos.XmlAddin(Path.Combine(carpeta, "addin.dll")));
    }

    private IEnumerable<string> Versiones(string id) =>
        Directory.Exists(_rutas.CarpetaVersiones(id))
            ? Directory.GetDirectories(_rutas.CarpetaVersiones(id)).Select(Path.GetFileName).OrderBy(v => v)!
            : [];

    [Fact]
    public void Una_version_activa_sin_licencia_se_borra_con_su_addin()
    {
        Instalar("acero-vigas", "1.0.1", "2026");

        LimpiezaVersionesViejas.Ejecutar(_rutas);

        Assert.False(File.Exists(_rutas.ArchivoAddin("acero-vigas", "2026")));
        Assert.Empty(Versiones("acero-vigas"));
    }

    [Fact]
    public void Solo_queda_la_version_activa()
    {
        Instalar("acero-vigas", "1.0.1", "2026", activa: false);
        Instalar("acero-vigas", "1.1.0", "2026", activa: false);
        Instalar("acero-vigas", "1.2.0", "2026");

        LimpiezaVersionesViejas.Ejecutar(_rutas);

        Assert.Equal(new[] { "1.2.0" }, Versiones("acero-vigas"));
        Assert.True(File.Exists(_rutas.ArchivoAddin("acero-vigas", "2026")));
    }

    [Fact]
    public void Se_borran_los_addin_de_antes_del_gestor_en_cada_anio()
    {
        foreach (string anio in new[] { "2026", "2027" })
        {
            Directory.CreateDirectory(_rutas.CarpetaAddinsRevit(anio));
            File.WriteAllText(Path.Combine(_rutas.CarpetaAddinsRevit(anio), "practicacq.addin"), "<RevitAddIns/>");
        }
        File.WriteAllText(Path.Combine(_rutas.CarpetaAddinsRevit("2026"), "CQEING_CuadroCantidades.addin"), "<RevitAddIns/>");
        File.WriteAllText(Path.Combine(_rutas.CarpetaAddinsRevit("2026"), "Otro.addin"), "<RevitAddIns/>");

        LimpiezaVersionesViejas.Ejecutar(_rutas);

        Assert.Equal(new[] { "Otro.addin" }, Directory.GetFiles(_rutas.CarpetaAddinsRevit("2026")).Select(Path.GetFileName));
        Assert.Empty(Directory.GetFiles(_rutas.CarpetaAddinsRevit("2027")));
    }

    [Fact]
    public void Una_compilacion_de_desarrollo_y_un_addin_nuevo_sin_minimo_no_se_tocan()
    {
        Directory.CreateDirectory(_rutas.CarpetaAddinsRevit("2026"));
        string desarrollo = Datos.XmlAddin(@"C:\repo\addins\acero-vigas\bin\Debug\2026\CQEING.Vigas.dll");
        File.WriteAllText(_rutas.ArchivoAddin("acero-vigas", "2026"), desarrollo);
        Instalar("otro-addin", "0.1.0", "2026");

        LimpiezaVersionesViejas.Ejecutar(_rutas);

        Assert.Equal(desarrollo, File.ReadAllText(_rutas.ArchivoAddin("acero-vigas", "2026")));
        Assert.Equal(new[] { "0.1.0" }, Versiones("otro-addin"));
    }

    [Fact]
    public void Sin_nada_instalado_no_falla()
    {
        LimpiezaVersionesViejas.Ejecutar(_rutas);
    }
}
