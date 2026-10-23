using System.Net;
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class GestorAddinsTests : IDisposable
{
    private const string UrlReleases = "https://api.github.com/repos/dueno/repo/releases?per_page=100";

    private readonly CarpetaTemporal _tmp = new();
    private readonly RutasGestor _rutas;
    // Una respuesta HTTP se consume al leerla: el servidor falso crea una nueva en cada pedido.
    private readonly Dictionary<string, Func<HttpResponseMessage>> _servidor = [];
    private readonly GestorAddins _gestor;
    private bool _sinRed;

    public GestorAddinsTests()
    {
        _rutas = new RutasGestor(_tmp.Combinar("local"), _tmp.Combinar("roaming"));
        Directory.CreateDirectory(_tmp.Combinar("pf", "Autodesk", "Revit 2026"));
        File.WriteAllText(_tmp.Combinar("pf", "Autodesk", "Revit 2026", "RevitAPI.dll"), "");

        var handler = new HandlerFalso(p =>
        {
            if (_sinRed) throw new HttpRequestException("sin red");
            return _servidor.TryGetValue(p.RequestUri!.ToString(), out Func<HttpResponseMessage>? r) ? r() : Respuestas.Estado(HttpStatusCode.NotFound);
        });
        HttpClient http = handler.Cliente();
        _gestor = new GestorAddins(
            new FuenteReleases(http, _rutas.CarpetaCache, "dueno/repo"),
            new ConstructorCatalogo(http, _rutas.CarpetaCache),
            new Instalador(http, _rutas),
            _rutas,
            _tmp.Combinar("pf"));
    }

    public void Dispose() => _tmp.Dispose();

    /// <summary>Publica en el servidor falso las versiones dadas de armado-columnas, más un gestor-v0.2.0 y una prerelease.</summary>
    private void Publicar(params string[] versiones)
    {
        var releases = new List<object>
        {
            new { tag_name = "gestor-v0.2.0", draft = false, prerelease = false, assets = Array.Empty<object>() },
            new { tag_name = "armado-columnas-v9.0.0-rc.1", draft = false, prerelease = true, assets = Array.Empty<object>() },
        };
        foreach (string version in versiones)
        {
            var (publicada, urlZip, zip) = Datos.VersionConZip(version);
            string urlManifiesto = $"https://github.test/armado-columnas-v{version}/manifest.json";
            _servidor[urlZip] = () => Respuestas.Bytes(zip);
            _servidor[urlManifiesto] = () => Respuestas.Texto(SerializadorManifiesto.Escribir(publicada.Manifiesto));
            releases.Add(new
            {
                tag_name = $"armado-columnas-v{version}",
                draft = false,
                prerelease = false,
                assets = new[]
                {
                    new { name = "manifest.json", browser_download_url = urlManifiesto },
                    new { name = publicada.Manifiesto.Paquetes[0].Archivo, browser_download_url = urlZip },
                },
            });
        }
        string json = System.Text.Json.JsonSerializer.Serialize(releases);
        _servidor[UrlReleases] = () => Respuestas.Texto(json);
    }

    [Fact]
    public async Task Instalar_y_ver_que_hay_una_version_nueva()
    {
        Publicar("1.0.0");
        ResumenGestor antes = await _gestor.ConsultarAsync();
        Assert.Equal(new[] { "2026" }, antes.AniosRevit);
        Assert.Equal("gestor-v0.2.0", antes.UltimoGestor!.ToString());
        EstadoAddin armado = Assert.Single(antes.Addins);
        Assert.Null(armado.Instalada("2026"));
        Assert.False(armado.HayActualizacion("2026"));

        await _gestor.InstalarAsync(armado.Addin.Ultima, "2026");
        Publicar("1.0.0", "1.1.0");
        ResumenGestor despues = await _gestor.ConsultarAsync();

        EstadoAddin actualizado = despues.Addins.Single();
        Assert.Equal("1.0.0", actualizado.Instalada("2026"));
        Assert.True(actualizado.HayActualizacion("2026"));
        Assert.True(GestorAddins.HayActualizacion(actualizado, "2026"));
        Assert.False(actualizado.HayActualizacion("2027"));
        Assert.Empty(despues.Avisos);
        Assert.False(despues.DesdeCache);
    }

    [Fact]
    public async Task La_prerelease_y_el_gestor_no_aparecen_como_add_ins()
    {
        Publicar("1.0.0");

        ResumenGestor resumen = await _gestor.ConsultarAsync();

        Assert.Equal(new[] { "armado-columnas" }, resumen.Addins.Select(a => a.Addin.Id));
        Assert.DoesNotContain(resumen.Addins.Single().Addin.Versiones, v => v.Version.ToString().Contains("rc"));
    }

    [Fact]
    public async Task Actualizar_deja_la_nueva_activa_y_la_vieja_en_disco_para_volver()
    {
        Publicar("1.0.0", "1.1.0");
        EstadoAddin armado = (await _gestor.ConsultarAsync()).Addins.Single();
        VersionPublicada v10 = armado.Addin.Versiones.Single(v => v.Version.ToString() == "1.0.0");
        VersionPublicada v11 = armado.Addin.Versiones.Single(v => v.Version.ToString() == "1.1.0");

        await _gestor.InstalarAsync(v10, "2026");
        await _gestor.InstalarAsync(v11, "2026");
        EstadoAddin despues = (await _gestor.ConsultarAsync()).Addins.Single();

        Assert.Equal("1.1.0", despues.Instalada("2026"));
        Assert.True(despues.EstaEnDisco("2026", "1.0.0"));
        Assert.True(despues.EstaEnDisco("2026", "1.1.0"));
        Assert.False(despues.HayActualizacion("2026"));
    }

    [Fact]
    public async Task Sin_conexion_con_lista_guardada_avisa_y_solo_deja_volver_a_lo_que_esta_en_disco()
    {
        Publicar("1.0.0", "1.1.0");
        EstadoAddin armado = (await _gestor.ConsultarAsync()).Addins.Single();
        VersionPublicada v10 = armado.Addin.Versiones.Single(v => v.Version.ToString() == "1.0.0");
        VersionPublicada v11 = armado.Addin.Versiones.Single(v => v.Version.ToString() == "1.1.0");
        await _gestor.InstalarAsync(v10, "2026");

        _sinRed = true;
        ResumenGestor sinRed = await _gestor.ConsultarAsync();
        EstadoAddin estado = sinRed.Addins.Single();

        Assert.True(sinRed.DesdeCache);
        Assert.Contains("última lista guardada", Assert.Single(sinRed.Avisos));
        Assert.True(estado.EstaEnDisco("2026", "1.0.0"));
        Assert.False(estado.EstaEnDisco("2026", "1.1.0"));
        await _gestor.InstalarAsync(v10, "2026"); // en disco: funciona sin red
        var error = await Assert.ThrowsAsync<GestorException>(() => _gestor.InstalarAsync(v11, "2026")); // no está en disco
        Assert.Equal(TipoError.SinConexion, error.Tipo);
        Assert.Equal("1.0.0", (await _gestor.ConsultarAsync()).Addins.Single().Instalada("2026"));
    }

    [Fact]
    public async Task Sin_conexion_y_sin_lista_guardada_lanza_SinConexion_y_no_inventa_versiones()
    {
        _sinRed = true;

        var error = await Assert.ThrowsAsync<GestorException>(() => _gestor.ConsultarAsync());

        Assert.Equal(TipoError.SinConexion, error.Tipo);
    }

    [Fact]
    public async Task Un_addin_de_desarrollo_o_danado_no_rompe_la_consulta()
    {
        Publicar("1.0.0");
        Directory.CreateDirectory(_rutas.CarpetaAddinsRevit("2026"));
        File.WriteAllText(_rutas.ArchivoAddin("armado-columnas", "2026"), Datos.XmlAddin(@"C:\repo\addins\armado-columnas\bin\Debug\2026\practicacq.dll"));
        File.WriteAllText(_rutas.ArchivoAddin("imagenes", "2026"), "<<< no es xml");

        EstadoAddin armado = (await _gestor.ConsultarAsync()).Addins.Single();

        Assert.Null(armado.Instalada("2026"));
    }

    [Fact]
    public async Task Sin_paquete_para_su_Revit_la_version_nueva_no_cuenta_como_actualizacion()
    {
        Publicar("1.0.0");
        EstadoAddin armado = (await _gestor.ConsultarAsync()).Addins.Single();
        await _gestor.InstalarAsync(armado.Addin.Ultima, "2026");

        var sinPaquete = armado.Addin.Ultima with
        {
            Version = VersionAddin.Interpretar("2.0.0")!,
            Manifiesto = Datos.Manifiesto(version: "2.0.0", paquetes: [Datos.Paquete("2027", new string('a', 64), version: "2.0.0")]),
        };
        var estado = new EstadoAddin(armado.Addin with { Versiones = [sinPaquete, armado.Addin.Ultima] }, new Dictionary<string, string> { ["2026"] = "1.0.0" });

        Assert.False(estado.HayActualizacion("2026"));
    }

    [Fact]
    public async Task Releer_el_estado_despues_de_instalar_no_consulta_GitHub()
    {
        Publicar("1.0.0", "1.1.0");
        ResumenGestor antes = await _gestor.ConsultarAsync();
        EstadoAddin armado = antes.Addins.Single();
        await _gestor.InstalarAsync(armado.Addin.Versiones.Single(v => v.Version.ToString() == "1.0.0"), "2026");

        _sinRed = true; // si ReleerEstado pidiera algo a GitHub, fallaría
        ResumenGestor despues = _gestor.ReleerEstado(antes);

        EstadoAddin estado = despues.Addins.Single();
        Assert.Equal("1.0.0", estado.Instalada("2026"));
        Assert.True(estado.EstaEnDisco("2026", "1.0.0"));
        Assert.True(estado.HayActualizacion("2026"));
        Assert.Same(armado.Addin, estado.Addin);
        Assert.Equal(antes.AniosRevit, despues.AniosRevit);
        Assert.Equal(antes.UltimoGestor, despues.UltimoGestor);
        Assert.Null(antes.Addins.Single().Instalada("2026")); // el resumen anterior no cambia
    }

    [Fact]
    public async Task Cancelar_la_consulta_se_propaga()
    {
        Publicar("1.0.0");
        using var cancelado = new CancellationTokenSource();
        cancelado.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _gestor.ConsultarAsync(cancelado.Token));
    }

    [Fact]
    public async Task Novedades_de_Revit_solo_de_lo_instalado_en_ese_anio()
    {
        Publicar("1.0.0");
        Assert.Empty(await _gestor.NovedadesAsync("2026", TimeSpan.Zero)); // publicado pero no instalado: no se avisa

        EstadoAddin armado = (await _gestor.ConsultarAsync()).Addins.Single();
        await _gestor.InstalarAsync(armado.Addin.Ultima, "2026");
        Assert.Empty(await _gestor.NovedadesAsync("2026", TimeSpan.Zero)); // al día

        Publicar("1.0.0", "1.1.0");
        Novedad novedad = Assert.Single(await _gestor.NovedadesAsync("2026", TimeSpan.Zero));

        Assert.Equal(new Novedad("armado-columnas", armado.Addin.Nombre, "1.0.0", "1.1.0"), novedad);
        Assert.Empty(await _gestor.NovedadesAsync("2027", TimeSpan.Zero)); // en 2027 no está instalado
    }

    [Fact]
    public async Task Novedades_con_lista_reciente_no_consultan_GitHub()
    {
        Publicar("1.0.0");
        EstadoAddin armado = (await _gestor.ConsultarAsync()).Addins.Single();
        await _gestor.InstalarAsync(armado.Addin.Ultima, "2026");
        Publicar("1.0.0", "1.1.0"); // GitHub ya tiene la 1.1.0, pero la lista guardada es de hace un momento

        _sinRed = true; // si consultara, usaría la guardada con aviso; tampoco debe intentarlo
        IReadOnlyList<Novedad> novedades = await _gestor.NovedadesAsync("2026", TimeSpan.FromMinutes(30));

        Assert.Empty(novedades);
    }

    [Fact]
    public void Sin_paquete_para_ese_Revit_no_hay_novedad()
    {
        var manifiesto = Datos.Manifiesto(version: "2.0.0", paquetes: [Datos.Paquete("2027", new string('a', 64), version: "2.0.0")]);
        var nueva = new VersionPublicada(manifiesto, VersionAddin.Interpretar("2.0.0")!, new Dictionary<string, Uri>());
        var estado = new EstadoAddin(
            new EntradaCatalogo("armado-columnas", "Armado de columnas", "", [nueva]),
            new Dictionary<string, string> { ["2026"] = "1.0.0", ["2027"] = "1.0.0" });

        Assert.Empty(NovedadesRevit.De([estado], "2026"));
        Assert.Equal("2.0.0", Assert.Single(NovedadesRevit.De([estado], "2027")).Nueva);
    }
}
