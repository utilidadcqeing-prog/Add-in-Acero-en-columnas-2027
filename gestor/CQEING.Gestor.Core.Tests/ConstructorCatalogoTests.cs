using System.Net;
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class ConstructorCatalogoTests : IDisposable
{
    private readonly CarpetaTemporal _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private ConstructorCatalogo Catalogo(HandlerFalso handler) => new(handler.Cliente(), _tmp.Ruta);

    [Fact]
    public async Task Solo_las_5_estables_mas_nuevas_por_addin_y_sin_el_gestor()
    {
        var releases = new List<ReleaseGitHub>
        {
            Datos.Release("armado-columnas-v1.7.0-rc.1", prerelease: true),
            Datos.Release("armado-columnas-v2.0.0", borrador: true),
            Datos.Release("imagenes-v1.0.0"),
            Datos.Release("gestor-v0.2.0"),
            Datos.Release("otra-cosa"),
        };
        releases.AddRange(Enumerable.Range(0, 7).Select(i => Datos.Release($"armado-columnas-v1.{i}.0")));
        var handler = new HandlerFalso(Datos.ManifiestoDe);

        ResultadoCatalogo r = await Catalogo(handler).ConstruirAsync(releases);

        Assert.Equal(new[] { "armado-columnas", "imagenes" }, r.Addins.Select(a => a.Id));
        EntradaCatalogo armado = r.Addins[0];
        Assert.Equal("Armado de columnas", armado.Nombre);
        Assert.Equal(new[] { "1.6.0", "1.5.0", "1.4.0", "1.3.0", "1.2.0" }, armado.Versiones.Select(v => v.Version.ToString()));
        Assert.Equal("1.6.0", armado.Ultima.Manifiesto.Version);
        Assert.Empty(r.Avisos);
        // Solo se descargan los manifiestos que se muestran: 5 de armado + 1 de imágenes.
        Assert.Equal(6, handler.Pedidos.Count);
    }

    [Fact]
    public async Task Prerelease_marcada_en_GitHub_o_con_sufijo_rc_no_aparece_aunque_sea_la_mas_nueva()
    {
        var releases = new[]
        {
            Datos.Release("imagenes-v2.0.0", prerelease: true),
            Datos.Release("imagenes-v2.0.0-rc.1"),
            Datos.Release("imagenes-v1.0.0"),
        };

        ResultadoCatalogo r = await Catalogo(new HandlerFalso(Datos.ManifiestoDe)).ConstruirAsync(releases);

        Assert.Equal(new[] { "1.0.0" }, r.Addins.Single().Versiones.Select(v => v.Version.ToString()));
    }

    [Fact]
    public async Task Ordena_por_numero_no_por_texto()
    {
        var releases = new[] { Datos.Release("imagenes-v1.9.0"), Datos.Release("imagenes-v1.10.0"), Datos.Release("imagenes-v1.2.0") };

        ResultadoCatalogo r = await Catalogo(new HandlerFalso(Datos.ManifiestoDe)).ConstruirAsync(releases);

        Assert.Equal(new[] { "1.10.0", "1.9.0", "1.2.0" }, r.Addins.Single().Versiones.Select(v => v.Version.ToString()));
    }

    [Fact]
    public async Task Sin_releases_el_catalogo_esta_vacio_sin_inventar_nada()
    {
        var handler = new HandlerFalso(Datos.ManifiestoDe);

        ResultadoCatalogo r = await Catalogo(handler).ConstruirAsync([]);

        Assert.Empty(r.Addins);
        Assert.Empty(r.Avisos);
        Assert.Empty(handler.Pedidos);
    }

    [Fact]
    public async Task Los_manifiestos_se_descargan_una_sola_vez()
    {
        var releases = new[] { Datos.Release("imagenes-v1.0.0") };
        var handler = new HandlerFalso(Datos.ManifiestoDe);
        var constructor = Catalogo(handler);

        await constructor.ConstruirAsync(releases);
        ResultadoCatalogo r = await constructor.ConstruirAsync(releases);

        Assert.Single(handler.Pedidos);
        Assert.Equal("1.0.0", r.Addins.Single().Ultima.Manifiesto.Version);
    }

    [Fact]
    public async Task Un_manifiesto_guardado_y_danado_se_descarta_y_se_pide_de_nuevo()
    {
        var releases = new[] { Datos.Release("imagenes-v1.0.0") };
        var handler = new HandlerFalso(Datos.ManifiestoDe);
        await Catalogo(handler).ConstruirAsync(releases);
        File.WriteAllText(Path.Combine(_tmp.Ruta, "manifiestos", "imagenes-v1.0.0.json"), "{ dañado");

        ResultadoCatalogo primero = await Catalogo(handler).ConstruirAsync(releases);
        ResultadoCatalogo segundo = await Catalogo(handler).ConstruirAsync(releases);

        Assert.Empty(primero.Addins);                  // esta vez se omite con aviso...
        Assert.Single(primero.Avisos);
        Assert.Single(segundo.Addins);                 // ...y la siguiente lo vuelve a descargar bien
        Assert.Equal(2, handler.Pedidos.Count);
    }

    [Fact]
    public async Task Release_sin_manifiesto_o_que_no_coincide_se_omite_con_aviso()
    {
        var releases = new[]
        {
            Datos.Release("imagenes-v1.0.0", conManifiesto: false),
            Datos.Release("imagenes-v1.1.0"),
        };
        var handler = new HandlerFalso(_ => Respuestas.Texto(SerializadorManifiesto.Escribir(Datos.Manifiesto("imagenes", "9.9.9"))));

        ResultadoCatalogo r = await Catalogo(handler).ConstruirAsync(releases);

        Assert.Empty(r.Addins);
        Assert.Equal(2, r.Avisos.Count);
        Assert.Contains(r.Avisos, a => a.StartsWith("imagenes-v1.1.0") && a.Contains("9.9.9"));
        Assert.False(Directory.Exists(Path.Combine(_tmp.Ruta, "manifiestos")), "no se guarda lo que no pasó la validación");
    }

    [Fact]
    public async Task Manifiesto_de_esquema_nuevo_avisa_que_hay_que_actualizar_el_gestor()
    {
        var handler = new HandlerFalso(_ => Respuestas.Texto(SerializadorManifiesto.Escribir(Datos.Manifiesto("imagenes") with { Esquema = 2 })));

        ResultadoCatalogo r = await Catalogo(handler).ConstruirAsync([Datos.Release("imagenes-v1.0.0")]);

        Assert.Contains("Actualice el gestor", Assert.Single(r.Avisos));
    }

    [Theory]
    [InlineData("{ no es json")]
    [InlineData("null")]
    [InlineData("")]
    public async Task Manifiesto_ilegible_se_omite_con_aviso_sin_romper_el_resto(string cuerpo)
    {
        var handler = new HandlerFalso(p => p.RequestUri!.ToString().Contains("armado")
            ? Respuestas.Texto(cuerpo)
            : Datos.ManifiestoDe(p));
        var releases = new[] { Datos.Release("armado-columnas-v1.0.0"), Datos.Release("imagenes-v1.0.0") };

        ResultadoCatalogo r = await Catalogo(handler).ConstruirAsync(releases);

        Assert.Equal("imagenes", r.Addins.Single().Id);
        Assert.Single(r.Avisos);
    }

    [Fact]
    public async Task Manifiesto_con_archivo_con_ruta_se_omite_antes_de_descargar_nada()
    {
        var malo = Datos.Manifiesto("imagenes", paquetes: [Datos.Paquete("2026", new string('a', 64), "imagenes") with { Archivo = @"..\..\fuera.zip" }]);
        var handler = new HandlerFalso(_ => Respuestas.Texto(SerializadorManifiesto.Escribir(malo)));

        ResultadoCatalogo r = await Catalogo(handler).ConstruirAsync([Datos.Release("imagenes-v1.0.0")]);

        Assert.Empty(r.Addins);
        Assert.Contains("paquetes válidos", Assert.Single(r.Avisos));
    }

    [Fact]
    public async Task Sin_red_al_pedir_un_manifiesto_se_omite_con_aviso()
    {
        var handler = new HandlerFalso(_ => throw new HttpRequestException("sin red"));

        ResultadoCatalogo r = await Catalogo(handler).ConstruirAsync([Datos.Release("imagenes-v1.0.0")]);

        Assert.Empty(r.Addins);
        Assert.Contains("no se pudo descargar su manifiesto", Assert.Single(r.Avisos));
    }

    [Fact]
    public async Task Servidor_que_responde_error_al_pedir_un_manifiesto_se_omite_con_aviso()
    {
        var handler = new HandlerFalso(_ => Respuestas.Estado(HttpStatusCode.NotFound));

        ResultadoCatalogo r = await Catalogo(handler).ConstruirAsync([Datos.Release("imagenes-v1.0.0")]);

        Assert.Empty(r.Addins);
        Assert.Single(r.Avisos);
    }

    [Fact]
    public async Task Cancelar_se_propaga_y_no_se_confunde_con_un_aviso()
    {
        var handler = new HandlerFalso(Datos.ManifiestoDe);
        using var cancelado = new CancellationTokenSource();
        cancelado.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Catalogo(handler).ConstruirAsync([Datos.Release("imagenes-v1.0.0")], cancelado.Token));
    }

    [Fact]
    public async Task Archivos_con_nombre_repetido_no_rompen_el_catalogo()
    {
        var duplicada = new ReleaseGitHub("imagenes-v1.0.0", false, false,
        [
            new ArchivoRelease("manifest.json", new Uri("https://github.test/imagenes-v1.0.0/manifest.json")),
            new ArchivoRelease("manifest.json", new Uri("https://github.test/otro/manifest.json")),
        ]);

        ResultadoCatalogo r = await Catalogo(new HandlerFalso(Datos.ManifiestoDe)).ConstruirAsync([duplicada]);

        Assert.Single(r.Addins);
    }

    [Fact]
    public void Ultimo_gestor_es_la_version_estable_mas_alta()
    {
        var releases = new[] { Datos.Release("gestor-v0.9.0"), Datos.Release("gestor-v0.10.0"), Datos.Release("gestor-v1.0.0-rc.1", prerelease: true), Datos.Release("imagenes-v5.0.0") };
        Assert.Equal("gestor-v0.10.0", ConstructorCatalogo.UltimoGestor(releases)!.ToString());
        Assert.Null(ConstructorCatalogo.UltimoGestor([Datos.Release("imagenes-v1.0.0")]));
    }

    [Fact]
    public void UrlDe_un_paquete_que_la_release_no_trae_es_PaqueteInvalido()
    {
        var version = new VersionPublicada(Datos.Manifiesto(), VersionAddin.Interpretar("1.0.0")!, new Dictionary<string, Uri>());
        var error = Assert.Throws<GestorException>(() => version.UrlDe(version.Manifiesto.Paquetes[0]));
        Assert.Equal(TipoError.PaqueteInvalido, error.Tipo);
    }
}
