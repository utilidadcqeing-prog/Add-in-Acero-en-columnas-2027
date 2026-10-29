using System.Net;
using System.Xml.Linq;
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class InstaladorTests : IDisposable
{
    private const string Id = "armado-columnas";
    private readonly CarpetaTemporal _tmp = new();
    private readonly Dictionary<string, Func<HttpResponseMessage>> _servidor = [];
    private readonly HandlerFalso _handler;
    private readonly RutasGestor _rutas;
    private readonly Instalador _instalador;

    public InstaladorTests()
    {
        _rutas = new RutasGestor(_tmp.Combinar("local"), _tmp.Combinar("roaming"));
        _handler = new HandlerFalso(p => _servidor.TryGetValue(p.RequestUri!.ToString(), out Func<HttpResponseMessage>? r)
            ? r()
            : Respuestas.Estado(HttpStatusCode.NotFound));
        // Holgado: con 300 ms, un servidor de CI lento cortaba descargas sanas. El corte se prueba aparte, con su propio límite.
        _instalador = new Instalador(_handler.Cliente(), _rutas, TimeSpan.FromSeconds(30));
    }

    public void Dispose() => _tmp.Dispose();

    private VersionPublicada Publicar(string version, string anio = "2026", byte[]? zip = null, IReadOnlyList<string>? heredados = null)
    {
        var (publicada, url, bytes) = Datos.VersionConZip(version, anio, zip, heredados);
        _servidor[url] = () => Respuestas.Bytes(bytes);
        return publicada;
    }

    private string AssemblyActivo(string anio = "2026") =>
        XDocument.Load(_rutas.ArchivoAddin(Id, anio)).Descendants("Assembly").Single().Value;

    private IEnumerable<string> VersionesEnDisco() =>
        Directory.GetDirectories(_rutas.CarpetaVersiones(Id)).Select(Path.GetFileName).Order(StringComparer.Ordinal)!;

    /// <summary>No queda nada instalado ni a medias: sin carpetas de versión, sin .addin y sin restos de descarga.</summary>
    private void AssertNoQuedoNada(string version = "1.0.0")
    {
        Assert.False(Directory.Exists(_rutas.CarpetaVersion(Id, version, "2026")));
        Assert.False(File.Exists(_rutas.ArchivoAddin(Id, "2026")));
        string versiones = _rutas.CarpetaVersiones(Id);
        Assert.True(!Directory.Exists(versiones) || !Directory.EnumerateFileSystemEntries(versiones).Any(), "quedaron restos en la carpeta de versiones");
        Assert.True(!Directory.Exists(_rutas.CarpetaTemporal) || !Directory.EnumerateFileSystemEntries(_rutas.CarpetaTemporal).Any(), "quedaron restos en la carpeta temporal");
    }

    [Fact]
    public async Task Instala_en_su_carpeta_y_apunta_el_addin_con_ruta_absoluta()
    {
        await _instalador.InstalarAsync(Publicar("1.0.0"), "2026");

        string carpeta = _rutas.CarpetaVersion(Id, "1.0.0", "2026");
        Assert.True(File.Exists(Path.Combine(carpeta, "practicacq.dll")));
        Assert.True(File.Exists(Path.Combine(carpeta, "Resources", "icono16.png")));
        Assert.Equal(Path.Combine(carpeta, "practicacq.dll"), AssemblyActivo());
        Assert.True(Path.IsPathFullyQualified(AssemblyActivo()));
        Assert.Equal("2044e120-cc19-42a1-8018-a97f13adf192", XDocument.Load(_rutas.ArchivoAddin(Id, "2026")).Descendants("ClientId").Single().Value);
        Assert.False(File.Exists(_rutas.ArchivoAddin(Id, "2026") + ".tmp"));
        Assert.True(!Directory.Exists(_rutas.CarpetaTemporal) || !Directory.EnumerateFileSystemEntries(_rutas.CarpetaTemporal).Any());
    }

    [Fact]
    public async Task Reporta_el_progreso_de_la_descarga_hasta_el_100_por_ciento()
    {
        var avances = new List<double>();

        await _instalador.InstalarAsync(Publicar("1.0.0"), "2026", progreso: new ProgresoSincrono(avances.Add));

        Assert.NotEmpty(avances);
        Assert.Equal(1, avances[^1]);
        Assert.Equal(avances.Order(), avances);
    }

    [Fact]
    public async Task Hash_distinto_no_instala_nada_ni_deja_restos()
    {
        var (publicada, url, _) = Datos.VersionConZip("1.0.0");
        _servidor[url] = () => Respuestas.Bytes(Datos.ZipAddin(dll: "alterada.dll"));

        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(publicada, "2026"));

        Assert.Equal(TipoError.HashNoCoincide, error.Tipo);
        AssertNoQuedoNada();
    }

    [Fact]
    public async Task Zip_cortado_a_la_mitad_no_coincide_con_el_hash_y_no_instala_nada()
    {
        var (publicada, url, zip) = Datos.VersionConZip("1.0.0");
        _servidor[url] = () => Respuestas.Bytes(zip[..(zip.Length / 2)]);

        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(publicada, "2026"));

        Assert.Equal(TipoError.HashNoCoincide, error.Tipo);
        AssertNoQuedoNada();
    }

    [Fact]
    public async Task Conexion_que_se_cae_a_mitad_de_la_descarga_no_instala_nada_ni_deja_restos()
    {
        var (publicada, url, zip) = Datos.VersionConZip("1.0.0");
        _servidor[url] = () => FlujosFalsos.Cortada(zip, zip.Length / 2);

        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(publicada, "2026"));

        Assert.Equal(TipoError.SinConexion, error.Tipo);
        AssertNoQuedoNada();
    }

    [Fact]
    public async Task Servidor_que_deja_de_mandar_datos_corta_la_descarga_sin_dejar_restos()
    {
        var (publicada, url, zip) = Datos.VersionConZip("1.0.0");
        _servidor[url] = () => FlujosFalsos.Colgada(zip, zip.Length / 2);
        var instalador = new Instalador(_handler.Cliente(), _rutas, TimeSpan.FromMilliseconds(300));

        var error = await Assert.ThrowsAsync<GestorException>(() => instalador.InstalarAsync(publicada, "2026"));

        Assert.Equal(TipoError.SinConexion, error.Tipo);
        AssertNoQuedoNada();
    }

    [Fact]
    public async Task Cancelar_la_descarga_no_deja_restos_y_no_se_confunde_con_un_error_de_red()
    {
        var (publicada, url, zip) = Datos.VersionConZip("1.0.0");
        _servidor[url] = () => FlujosFalsos.Colgada(zip, zip.Length / 2);
        var instalador = new Instalador(_handler.Cliente(), _rutas, TimeSpan.FromMinutes(5));
        using var cancelar = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => instalador.InstalarAsync(publicada, "2026", cancelar.Token));

        AssertNoQuedoNada();
    }

    [Fact]
    public async Task Servidor_que_responde_404_es_SinConexion_y_no_instala_nada()
    {
        var (publicada, _, _) = Datos.VersionConZip("1.0.0"); // no se publica el ZIP

        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(publicada, "2026"));

        Assert.Equal(TipoError.SinConexion, error.Tipo);
        AssertNoQuedoNada();
    }

    [Fact]
    public async Task Sin_paquete_para_ese_Revit_no_descarga_nada()
    {
        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(Publicar("1.0.0", anio: "2026"), "2027"));

        Assert.Equal(TipoError.SinPaqueteParaRevit, error.Tipo);
        Assert.Contains("Revit 2027", error.Message);
        Assert.Empty(_handler.Pedidos);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("20x6")]
    [InlineData("")]
    public async Task Anio_de_Revit_invalido_es_error_controlado_y_no_descarga_nada(string anio)
    {
        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(Publicar("1.0.0"), anio));

        Assert.Equal(TipoError.SinPaqueteParaRevit, error.Tipo);
        Assert.Empty(_handler.Pedidos);
    }

    [Theory]
    [InlineData(@"..\..\fuera")]
    [InlineData("Con Mayusculas")]
    public async Task Id_del_manifiesto_con_ruta_se_rechaza_antes_de_tocar_el_disco(string id)
    {
        var (valida, url, zip) = Datos.VersionConZip("1.0.0");
        var peligrosa = valida with { Manifiesto = valida.Manifiesto with { Id = id } };
        _servidor[url] = () => Respuestas.Bytes(zip);

        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(peligrosa, "2026"));

        Assert.Equal(TipoError.ManifiestoInvalido, error.Tipo);
        Assert.Empty(_handler.Pedidos);
        Assert.False(Directory.Exists(_tmp.Combinar("local")));
    }

    [Fact]
    public async Task Volver_a_una_version_en_disco_no_descarga_nada()
    {
        VersionPublicada v1 = Publicar("1.0.0");
        await _instalador.InstalarAsync(v1, "2026");
        await _instalador.InstalarAsync(Publicar("1.1.0"), "2026");
        _handler.Pedidos.Clear();
        _servidor.Clear(); // sin red

        await _instalador.InstalarAsync(v1, "2026");

        Assert.Empty(_handler.Pedidos);
        Assert.Contains(@"\1.0.0\2026\", AssemblyActivo());
    }

    [Fact]
    public async Task Carpeta_de_version_vacia_o_danada_se_vuelve_a_descargar()
    {
        Directory.CreateDirectory(_rutas.CarpetaVersion(Id, "1.0.0", "2026")); // a medias: sin DLL ni .addin

        await _instalador.InstalarAsync(Publicar("1.0.0"), "2026");

        Assert.True(File.Exists(Path.Combine(_rutas.CarpetaVersion(Id, "1.0.0", "2026"), "practicacq.dll")));
        Assert.Single(_handler.Pedidos);
        Assert.Equal(new[] { "1.0.0" }, VersionesEnDisco());
    }

    [Fact]
    public async Task Retira_los_addin_heredados_y_solo_esos()
    {
        string carpetaRevit = _rutas.CarpetaAddinsRevit("2026");
        Directory.CreateDirectory(carpetaRevit);
        File.WriteAllText(Path.Combine(carpetaRevit, "practicacq.addin"), Datos.XmlAddin("practicacq.dll"));
        File.WriteAllText(Path.Combine(carpetaRevit, "practicacq.dll"), "dll vieja");
        File.WriteAllText(Path.Combine(carpetaRevit, "OtroFabricante.addin"), "<RevitAddIns/>");

        await _instalador.InstalarAsync(Publicar("1.0.0", heredados: ["practicacq.addin"]), "2026");

        Assert.False(File.Exists(Path.Combine(carpetaRevit, "practicacq.addin")));
        Assert.True(File.Exists(Path.Combine(carpetaRevit, "OtroFabricante.addin")));
        Assert.True(File.Exists(Path.Combine(carpetaRevit, "practicacq.dll"))); // sin su .addin, Revit no la carga
    }

    [Fact]
    public async Task Heredados_de_otro_anio_de_Revit_no_se_tocan()
    {
        string carpeta2027 = _rutas.CarpetaAddinsRevit("2027");
        Directory.CreateDirectory(carpeta2027);
        File.WriteAllText(Path.Combine(carpeta2027, "practicacq.addin"), Datos.XmlAddin("practicacq.dll"));

        await _instalador.InstalarAsync(Publicar("1.0.0", heredados: ["practicacq.addin"]), "2026");

        Assert.True(File.Exists(Path.Combine(carpeta2027, "practicacq.addin")));
    }

    [Fact]
    public async Task Conserva_las_3_versiones_mas_nuevas_y_las_activas()
    {
        await _instalador.InstalarAsync(Publicar("1.0.0", anio: "2027"), "2027");
        foreach (string version in new[] { "1.1.0", "1.2.0", "1.3.0", "1.4.0" })
            await _instalador.InstalarAsync(Publicar(version), "2026");

        // 1.0.0 sigue activa en Revit 2027; 1.1.0 es la cuarta más nueva y no está activa.
        Assert.Equal(new[] { "1.0.0", "1.2.0", "1.3.0", "1.4.0" }, VersionesEnDisco());
    }

    [Fact]
    public async Task La_limpieza_no_toca_carpetas_que_no_son_versiones()
    {
        foreach (string version in new[] { "1.0.0", "1.1.0", "1.2.0", "1.3.0" })
            await _instalador.InstalarAsync(Publicar(version), "2026");
        string ajena = Path.Combine(_rutas.CarpetaVersiones(Id), "notas-del-usuario");
        Directory.CreateDirectory(ajena);
        File.WriteAllText(Path.Combine(ajena, "a.txt"), "mío");

        await _instalador.InstalarAsync(Publicar("1.4.0"), "2026");

        Assert.True(File.Exists(Path.Combine(ajena, "a.txt")));
        Assert.Equal(new[] { "1.2.0", "1.3.0", "1.4.0", "notas-del-usuario" }, VersionesEnDisco());
    }

    [Fact]
    public async Task Version_vieja_bloqueada_no_rompe_y_se_borra_despues()
    {
        foreach (string version in new[] { "1.0.0", "1.1.0", "1.2.0" })
            await _instalador.InstalarAsync(Publicar(version), "2026");
        string dllVieja = Path.Combine(_rutas.CarpetaVersion(Id, "1.0.0", "2026"), "practicacq.dll");

        using (File.Open(dllVieja, FileMode.Open, FileAccess.Read, FileShare.None)) // Revit la tiene cargada
        {
            await _instalador.InstalarAsync(Publicar("1.3.0"), "2026");
            Assert.True(File.Exists(Path.Combine(_rutas.CarpetaVersion(Id, "1.0.0", "2026"), "practicacq.addin"))); // intacta, no a medias
            Assert.True(File.Exists(dllVieja));
            Assert.Contains(@"\1.3.0\2026\", AssemblyActivo()); // la nueva quedó activa igual
        }

        await _instalador.InstalarAsync(Publicar("1.3.0"), "2026");
        Assert.Equal(new[] { "1.1.0", "1.2.0", "1.3.0" }, VersionesEnDisco());
    }

    [Theory]
    [InlineData(2, null)]
    [InlineData(1, @"C:\otra\carpeta\practicacq.dll")]
    public async Task Paquete_mal_armado_es_PaqueteInvalido_y_no_queda_instalado(int addins, string? assembly)
    {
        VersionPublicada publicada = Publicar("1.0.0", zip: Datos.ZipAddin(addins: addins, assembly: assembly));

        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(publicada, "2026"));

        Assert.Equal(TipoError.PaqueteInvalido, error.Tipo);
        AssertNoQuedoNada();
    }

    [Fact]
    public async Task Addin_que_nombra_una_dll_ausente_es_PaqueteInvalido()
    {
        VersionPublicada publicada = Publicar("1.0.0", zip: Datos.ZipAddin(incluirDll: false));

        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(publicada, "2026"));

        Assert.Equal(TipoError.PaqueteInvalido, error.Tipo);
        AssertNoQuedoNada();
    }

    [Fact]
    public async Task Addin_danado_dentro_del_paquete_es_PaqueteInvalido()
    {
        VersionPublicada publicada = Publicar("1.0.0", zip: Datos.ZipAddin(xml: "<<< no es xml"));

        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(publicada, "2026"));

        Assert.Equal(TipoError.PaqueteInvalido, error.Tipo);
        AssertNoQuedoNada();
    }

    [Fact]
    public async Task Zip_con_entrada_que_se_sale_de_la_carpeta_no_escribe_fuera_y_no_instala()
    {
        VersionPublicada publicada = Publicar("1.0.0", zip: Datos.ZipAddin(entradaExtra: "../../fuera.txt"));

        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(publicada, "2026"));

        Assert.Equal(TipoError.PaqueteInvalido, error.Tipo);
        Assert.False(File.Exists(Path.Combine(_tmp.Ruta, "fuera.txt")));
        Assert.False(File.Exists(Path.Combine(_tmp.Combinar("local"), "CQEING", "fuera.txt")));
        AssertNoQuedoNada();
    }

    [Fact]
    public async Task Archivo_que_no_es_un_zip_con_hash_correcto_es_PaqueteInvalido()
    {
        byte[] basura = "esto no es un zip"u8.ToArray();
        VersionPublicada publicada = Publicar("1.0.0", zip: basura);

        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(publicada, "2026"));

        Assert.Equal(TipoError.PaqueteInvalido, error.Tipo);
        AssertNoQuedoNada();
    }

    [Fact]
    public async Task Un_fallo_al_instalar_no_cambia_la_version_activa()
    {
        await _instalador.InstalarAsync(Publicar("1.0.0"), "2026");
        string antes = File.ReadAllText(_rutas.ArchivoAddin(Id, "2026"));
        var (mala, url, _) = Datos.VersionConZip("1.1.0");
        _servidor[url] = () => Respuestas.Bytes(Datos.ZipAddin(dll: "alterada.dll"));

        await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(mala, "2026"));

        Assert.Equal(antes, File.ReadAllText(_rutas.ArchivoAddin(Id, "2026")));
        Assert.Equal(new[] { "1.0.0" }, VersionesEnDisco());
    }

    [Fact]
    public async Task Temporales_viejos_de_una_descarga_que_se_cerro_de_golpe_se_limpian()
    {
        string resto = Path.Combine(_rutas.CarpetaTemporal, "abandonado");
        Directory.CreateDirectory(resto);
        File.WriteAllText(Path.Combine(resto, "paquete.zip"), "a medias");
        Directory.SetLastWriteTimeUtc(resto, DateTime.UtcNow.AddDays(-3));
        string reciente = Path.Combine(_rutas.CarpetaTemporal, "en-curso");
        Directory.CreateDirectory(reciente);

        await _instalador.InstalarAsync(Publicar("1.0.0"), "2026");

        Assert.False(Directory.Exists(resto));
        Assert.True(Directory.Exists(reciente)); // podría ser de otra instalación en curso
    }

    private sealed class ProgresoSincrono(Action<double> alReportar) : IProgress<double>
    {
        public void Report(double valor) => alReportar(valor);
    }
}
