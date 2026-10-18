using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class RutasYRevitTests
{
    [Fact]
    public void Rutas_de_la_spec()
    {
        var rutas = new RutasGestor(@"C:\U\Local", @"C:\U\Roaming");
        Assert.Equal(@"C:\U\Local\CQEING\Addins\imagenes\1.2.0\2026", rutas.CarpetaVersion("imagenes", "1.2.0", "2026"));
        Assert.Equal(@"C:\U\Roaming\Autodesk\Revit\Addins\2027\CQEING.imagenes.addin", rutas.ArchivoAddin("imagenes", "2027"));
        Assert.Equal(@"C:\U\Local\CQEING\Gestor\cache", rutas.CarpetaCache);
    }

    [Fact]
    public void Rutas_del_usuario_salen_de_LocalAppData_y_AppData()
    {
        var rutas = RutasGestor.DelUsuario();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.Equal(Path.Combine(local, "CQEING", "Addins", "armado-columnas"), rutas.CarpetaVersiones("armado-columnas"));
        Assert.Equal(Path.Combine(roaming, "Autodesk", "Revit", "Addins", "2026"), rutas.CarpetaAddinsRevit("2026"));
    }

    [Theory]
    [InlineData("..", "1.0.0", "2026")]
    [InlineData(@"..\otro", "1.0.0", "2026")]
    [InlineData("Armado", "1.0.0", "2026")]
    [InlineData("", "1.0.0", "2026")]
    [InlineData("imagenes", "..", "2026")]
    [InlineData("imagenes", @"1.0.0\..\..", "2026")]
    [InlineData("imagenes", "1.0", "2026")]
    [InlineData("imagenes", "1.0.0", "26")]
    [InlineData("imagenes", "1.0.0", @"2026\..")]
    public void Id_version_o_anio_con_ruta_se_rechazan(string id, string version, string anio)
    {
        var rutas = new RutasGestor(@"C:\U\Local", @"C:\U\Roaming");
        Assert.Throws<ArgumentException>(() => rutas.CarpetaVersion(id, version, anio));
    }

    [Fact]
    public void Archivo_addin_con_id_o_anio_invalido_se_rechaza()
    {
        var rutas = new RutasGestor(@"C:\U\Local", @"C:\U\Roaming");
        Assert.Throws<ArgumentException>(() => rutas.ArchivoAddin(@"..\..\x", "2026"));
        Assert.Throws<ArgumentException>(() => rutas.ArchivoAddin("imagenes", "Revit 2026"));
    }

    [Fact]
    public void Detecta_solo_carpetas_de_Revit_con_su_API()
    {
        using var tmp = new CarpetaTemporal();
        foreach (string carpeta in new[] { "Revit 2027", "Revit 2026", "Revit 2025", "Revit Viewer", "Revit Assistant 2027", "Revit 20x6", "AutoCAD 2026" })
            Directory.CreateDirectory(tmp.Combinar("Autodesk", carpeta));
        File.WriteAllText(tmp.Combinar("Autodesk", "Revit 2026", "RevitAPI.dll"), "");
        File.WriteAllText(tmp.Combinar("Autodesk", "Revit 2027", "RevitAPI.dll"), "");
        File.WriteAllText(tmp.Combinar("Autodesk", "Revit Assistant 2027", "RevitAPI.dll"), "");
        File.WriteAllText(tmp.Combinar("Autodesk", "AutoCAD 2026", "RevitAPI.dll"), "");

        // Revit 2025 tiene carpeta pero no la API (desinstalado a medias): no cuenta.
        Assert.Equal(new[] { "2026", "2027" }, DetectorRevit.AniosInstalados(tmp.Ruta));
    }

    [Fact]
    public void Sin_Autodesk_no_hay_Revit()
    {
        using var tmp = new CarpetaTemporal();
        Assert.Empty(DetectorRevit.AniosInstalados(tmp.Ruta));
    }

    [Fact]
    public void Program_Files_inexistente_no_hay_Revit() =>
        Assert.Empty(DetectorRevit.AniosInstalados(Path.Combine(Path.GetTempPath(), $"no-existe-{Guid.NewGuid():N}")));

    [Fact]
    public void Cliente_lleva_User_Agent_y_tiempo_maximo()
    {
        using HttpClient http = ClienteHttp.Crear();
        Assert.Equal(ClienteHttp.UserAgent, http.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Equal(ClienteHttp.TiempoMaximo, http.Timeout);
    }
}
