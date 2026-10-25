using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class ReinicioRevitTests : IDisposable
{
    private readonly CarpetaTemporal _tmp = new();
    private readonly RutasGestor _rutas;
    private readonly ProcesosFalsos _procesos = new();
    private DateTime _ahora = new(2026, 10, 25, 15, 0, 0, DateTimeKind.Utc);

    public ReinicioRevitTests() => _rutas = new RutasGestor(_tmp.Combinar("local"), _tmp.Combinar("roaming"));

    public void Dispose() => _tmp.Dispose();

    private ReinicioRevit Reinicio => new(_procesos, _rutas, TimeSpan.FromSeconds(1), () => _ahora);

    private string Modelo(string nombre)
    {
        string ruta = _tmp.Combinar(nombre);
        File.WriteAllText(ruta, "");
        return ruta;
    }

    [Theory]
    [InlineData(@"C:\Program Files\Autodesk\Revit 2027\Revit.exe", "2027")]
    [InlineData(@"D:\Autodesk\Revit 2026\Revit.exe", "2026")]
    [InlineData(@"C:\Program Files\Autodesk\Revit Viewer 2027\Revit.exe", null)]
    [InlineData(@"C:\otro\Revit.exe", null)]
    public void Reconoce_el_anio_por_la_carpeta_del_ejecutable(string ejecutable, string? anio) =>
        Assert.Equal(anio, ReinicioRevit.AnioDe(ejecutable));

    [Fact]
    public async Task Cierra_solo_el_Revit_de_ese_anio_y_lo_reabre_con_su_modelo()
    {
        _procesos.Agregar(10, "2026");
        RevitAbierto revit2027 = _procesos.Agregar(20, "2027");
        string modelo = Modelo("Edificio.rvt");
        SesionRevit.Anotar(_rutas, 20, modelo);

        ResultadoCierre cierre = await Reinicio.CerrarAsync(["2027"]);
        bool reabierto = Reinicio.Reabrir(cierre.Cerrados);

        Assert.Equal(new[] { 20 }, _procesos.CierresPedidos);
        Assert.Empty(cierre.SiguenAbiertos);
        Assert.True(reabierto);
        Assert.Equal((revit2027.Ejecutable, (string?)modelo), Assert.Single(_procesos.Abiertas));
        Assert.True(Reinicio.HayAbierto("2026"));
    }

    [Fact]
    public async Task Sin_anotacion_o_con_modelo_borrado_se_reabre_solo_Revit()
    {
        _procesos.Agregar(10, "2027");
        _procesos.Agregar(11, "2027");
        string borrado = Modelo("Viejo.rvt");
        SesionRevit.Anotar(_rutas, 11, borrado);
        File.Delete(borrado);

        Reinicio.Reabrir((await Reinicio.CerrarAsync(["2027"])).Cerrados);

        Assert.Equal(2, _procesos.Abiertas.Count);
        Assert.All(_procesos.Abiertas, a => Assert.Null(a.Modelo));
    }

    [Fact]
    public async Task Si_el_usuario_cancela_Revit_sigue_abierto_y_no_se_reabre()
    {
        _procesos.Agregar(20, "2027");
        _procesos.Cancelan.Add(20);

        ResultadoCierre cierre = await Reinicio.CerrarAsync(["2027"]);

        Assert.Empty(cierre.Cerrados);
        Assert.Equal(new[] { "2027" }, cierre.SiguenAbiertos);
        Assert.True(Reinicio.HayAbierto("2027"));
    }

    [Fact]
    public async Task Sin_anios_cierra_todos()
    {
        _procesos.Agregar(10, "2026");
        _procesos.Agregar(20, "2027");

        ResultadoCierre cierre = await Reinicio.CerrarAsync(null);

        Assert.Equal(2, cierre.Cerrados.Count);
        Assert.Empty(_procesos.Lista);
    }

    [Fact]
    public void Anotar_sin_modelo_borra_la_anotacion()
    {
        SesionRevit.Anotar(_rutas, 7, Modelo("A.rvt"));
        Assert.True(File.Exists(_rutas.ArchivoSesionRevit(7)));

        SesionRevit.Anotar(_rutas, 7, null);

        Assert.False(File.Exists(_rutas.ArchivoSesionRevit(7)));
    }

    [Fact]
    public void Los_pendientes_se_toman_una_sola_vez()
    {
        var revits = new[] { new RevitParaReabrir(@"C:\Program Files\Autodesk\Revit 2027\Revit.exe", @"C:\m\Edificio.rvt") };
        Reinicio.GuardarPendientes(revits);

        Assert.Equal(revits, Reinicio.TomarPendientes());
        Assert.Empty(Reinicio.TomarPendientes());
        Assert.False(File.Exists(_rutas.ArchivoReabrirRevit));
    }

    [Fact]
    public void Pendientes_viejos_o_danados_se_ignoran_y_se_borran()
    {
        Reinicio.GuardarPendientes([new RevitParaReabrir(@"C:\Program Files\Autodesk\Revit 2027\Revit.exe", null)]);
        _ahora += ReinicioRevit.VigenciaPendientes + TimeSpan.FromMinutes(1);
        Assert.Empty(Reinicio.TomarPendientes());

        File.WriteAllText(_rutas.ArchivoReabrirRevit, "{ dañado");
        Assert.Empty(Reinicio.TomarPendientes());
        Assert.False(File.Exists(_rutas.ArchivoReabrirRevit));
    }

    [Fact]
    public void Sin_Revit_cerrados_no_se_anota_nada()
    {
        Reinicio.GuardarPendientes([]);

        Assert.False(File.Exists(_rutas.ArchivoReabrirRevit));
    }

    [Fact]
    public void Si_Revit_no_abre_lo_dice()
    {
        _procesos.AbrirFalla = true;

        Assert.False(Reinicio.Reabrir([new RevitParaReabrir(@"C:\no\Revit 2027\Revit.exe", null)]));
    }
}
