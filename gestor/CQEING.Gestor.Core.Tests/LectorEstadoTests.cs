using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class LectorEstadoTests : IDisposable
{
    private readonly CarpetaTemporal _tmp = new();
    private readonly RutasGestor _rutas;

    public LectorEstadoTests() => _rutas = new RutasGestor(_tmp.Combinar("local"), _tmp.Combinar("roaming"));

    public void Dispose() => _tmp.Dispose();

    private void EscribirAddin(string id, string anio, string contenido)
    {
        Directory.CreateDirectory(_rutas.CarpetaAddinsRevit(anio));
        File.WriteAllText(_rutas.ArchivoAddin(id, anio), contenido);
    }

    [Fact]
    public void Lee_id_version_y_anio_de_los_addin_del_gestor()
    {
        EscribirAddin("imagenes", "2027", Datos.XmlAddin(Path.Combine(_rutas.CarpetaVersion("imagenes", "1.2.0", "2027"), "CQEING.Imagenes.dll")));

        Assert.Equal(new[] { new AddinInstalado("imagenes", "1.2.0", "2027") }, LectorEstado.Leer(_rutas));
    }

    [Fact]
    public void Addin_ajeno_o_danado_se_ignora()
    {
        EscribirAddin("armado-columnas", "2026", Datos.XmlAddin(@"C:\repo\addins\armado-columnas\bin\Debug\2026\practicacq.dll"));
        EscribirAddin("imagenes", "2026", "<<< no es xml");
        EscribirAddin("vacio", "2026", Datos.XmlAddin("practicacq.dll"));

        Assert.Empty(LectorEstado.Leer(_rutas));
    }

    [Fact]
    public void Un_addin_malo_no_impide_leer_los_buenos()
    {
        EscribirAddin("imagenes", "2026", "<<< no es xml");
        EscribirAddin("armado-columnas", "2026", Datos.XmlAddin(Path.Combine(_rutas.CarpetaVersion("armado-columnas", "1.0.0", "2026"), "practicacq.dll")));

        Assert.Equal(new[] { new AddinInstalado("armado-columnas", "1.0.0", "2026") }, LectorEstado.Leer(_rutas));
    }

    [Fact]
    public void Addin_con_nombre_que_no_es_un_id_valido_se_ignora_sin_fallar()
    {
        string carpeta = _rutas.CarpetaAddinsRevit("2026");
        Directory.CreateDirectory(carpeta);
        foreach (string nombre in new[] { "CQEING.Mal_Nombre.addin", "CQEING.MAYUS.addin", "CQEING..addin", "CQEING.con espacio.addin" })
            File.WriteAllText(Path.Combine(carpeta, nombre), Datos.XmlAddin(@"C:\x\Addins\x\1.0.0\2026\a.dll"));

        Assert.Empty(LectorEstado.Leer(_rutas));
    }

    [Fact]
    public void Carpetas_que_no_son_un_anio_de_Revit_se_ignoran()
    {
        string ajena = Path.Combine(_rutas.CarpetaAddinsRevitRaiz, "copia-de-seguridad");
        Directory.CreateDirectory(ajena);
        File.WriteAllText(Path.Combine(ajena, "CQEING.imagenes.addin"),
            Datos.XmlAddin(Path.Combine(_rutas.CarpetaVersion("imagenes", "1.0.0", "2026"), "a.dll")));

        Assert.Empty(LectorEstado.Leer(_rutas));
    }

    [Fact]
    public void Addin_de_un_anio_que_apunta_a_la_carpeta_de_otro_anio_se_ignora()
    {
        EscribirAddin("imagenes", "2026", Datos.XmlAddin(Path.Combine(_rutas.CarpetaVersion("imagenes", "1.0.0", "2027"), "a.dll")));

        Assert.Empty(LectorEstado.Leer(_rutas));
    }

    [Fact]
    public void Addin_que_apunta_a_una_version_con_nombre_raro_o_a_una_ruta_relativa_se_ignora()
    {
        EscribirAddin("imagenes", "2026", Datos.XmlAddin(Path.Combine(_rutas.CarpetaVersiones("imagenes"), "version-uno", "2026", "a.dll")));
        EscribirAddin("armado-columnas", "2026", Datos.XmlAddin(@"1.0.0\2026\practicacq.dll"));

        Assert.Empty(LectorEstado.Leer(_rutas));
    }

    [Fact]
    public void Sin_carpeta_de_Revit_no_hay_nada_instalado() => Assert.Empty(LectorEstado.Leer(_rutas));

    [Fact]
    public void Versiones_en_disco_lista_solo_las_de_ese_anio_de_la_mas_nueva_a_la_mas_vieja()
    {
        foreach ((string version, string anio) in new[] { ("1.9.0", "2026"), ("1.10.0", "2026"), ("1.2.0", "2027") })
            Directory.CreateDirectory(_rutas.CarpetaVersion("imagenes", version, anio));
        Directory.CreateDirectory(Path.Combine(_rutas.CarpetaVersiones("imagenes"), "notas"));

        Assert.Equal(new[] { "1.10.0", "1.9.0" }, LectorEstado.VersionesEnDisco(_rutas, "imagenes", "2026"));
        Assert.Equal(new[] { "1.2.0" }, LectorEstado.VersionesEnDisco(_rutas, "imagenes", "2027"));
        Assert.Empty(LectorEstado.VersionesEnDisco(_rutas, "armado-columnas", "2026"));
    }
}
