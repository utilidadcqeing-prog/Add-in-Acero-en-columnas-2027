using System.Net;
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class FuenteReleasesTests : IDisposable
{
    private const string Json = """
        [{ "tag_name": "armado-columnas-v1.0.0", "draft": false, "prerelease": false,
           "assets": [{ "name": "manifest.json", "browser_download_url": "https://github.test/a/manifest.json" }] }]
        """;

    private readonly CarpetaTemporal _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private FuenteReleases Fuente(HandlerFalso handler) => new(handler.Cliente(), _tmp.Ruta, "dueno/repo");

    /// <summary>Primera consulta bien (deja la lista guardada); las siguientes responden con <paramref name="despues"/>.</summary>
    private static HandlerFalso PrimeroBienDespues(Func<HttpResponseMessage> despues)
    {
        int llamada = 0;
        return new HandlerFalso(_ => ++llamada == 1 ? Respuestas.Texto(Json, "\"v1\"") : despues());
    }

    [Fact]
    public async Task Lee_las_releases_y_las_guarda_con_su_ETag()
    {
        var handler = new HandlerFalso(_ => Respuestas.Texto(Json, "\"v1\""));

        ResultadoReleases r = await Fuente(handler).ObtenerAsync();

        Assert.Null(r.Aviso);
        Assert.False(r.DesdeCache);
        ReleaseGitHub release = Assert.Single(r.Releases);
        Assert.Equal("armado-columnas-v1.0.0", release.Etiqueta);
        Assert.Equal(new Uri("https://github.test/a/manifest.json"), release.Archivos.Single().Url);
        Assert.Equal("https://api.github.com/repos/dueno/repo/releases?per_page=100", handler.Pedidos[0].RequestUri!.ToString());
        Assert.Contains("CQEING-Gestor", handler.Pedidos[0].Headers.UserAgent.ToString());
        Assert.Empty(handler.Pedidos[0].Headers.Authorization?.ToString() ?? ""); // sin token
    }

    [Fact]
    public async Task Sin_cambios_GitHub_responde_304_y_se_usa_la_lista_guardada()
    {
        var handler = PrimeroBienDespues(() => Respuestas.Estado(HttpStatusCode.NotModified));
        FuenteReleases fuente = Fuente(handler);
        await fuente.ObtenerAsync();

        ResultadoReleases r = await fuente.ObtenerAsync();

        Assert.Equal("\"v1\"", handler.Pedidos[1].Headers.IfNoneMatch.Single().ToString());
        Assert.Null(r.Aviso);
        Assert.Single(r.Releases);
    }

    [Fact]
    public async Task Limite_de_GitHub_usa_la_lista_guardada_con_aviso()
    {
        var handler = PrimeroBienDespues(() =>
        {
            var limitada = Respuestas.Estado(HttpStatusCode.Forbidden);
            limitada.Headers.Add("x-ratelimit-remaining", "0");
            return limitada;
        });
        FuenteReleases fuente = Fuente(handler);
        await fuente.ObtenerAsync();

        ResultadoReleases r = await fuente.ObtenerAsync();

        Assert.Single(r.Releases);
        Assert.Contains("limitó", r.Aviso);
        Assert.True(r.DesdeCache);
    }

    [Fact]
    public async Task Limite_de_GitHub_sin_lista_guardada_lanza_LimiteGitHub()
    {
        var handler = new HandlerFalso(_ => Respuestas.Estado(HttpStatusCode.TooManyRequests));
        var error = await Assert.ThrowsAsync<GestorException>(() => Fuente(handler).ObtenerAsync());
        Assert.Equal(TipoError.LimiteGitHub, error.Tipo);
    }

    [Fact]
    public async Task Sin_conexion_y_sin_lista_guardada_lanza_SinConexion()
    {
        var handler = new HandlerFalso(_ => throw new HttpRequestException("sin red"));
        var error = await Assert.ThrowsAsync<GestorException>(() => Fuente(handler).ObtenerAsync());
        Assert.Equal(TipoError.SinConexion, error.Tipo);
    }

    [Fact]
    public async Task Sin_conexion_con_lista_guardada_la_usa_con_aviso()
    {
        var handler = PrimeroBienDespues(() => throw new HttpRequestException("sin red"));
        FuenteReleases fuente = Fuente(handler);
        await fuente.ObtenerAsync();

        ResultadoReleases r = await fuente.ObtenerAsync();

        Assert.Single(r.Releases);
        Assert.Equal("No hay conexión con GitHub. Se muestra la última lista guardada.", r.Aviso);
    }

    [Fact]
    public async Task Lista_guardada_danada_con_304_se_vuelve_a_pedir_sin_ETag()
    {
        int llamada = 0;
        var handler = new HandlerFalso(p => ++llamada == 2 ? Respuestas.Estado(HttpStatusCode.NotModified) : Respuestas.Texto(Json, "\"v1\""));
        FuenteReleases fuente = Fuente(handler);
        await fuente.ObtenerAsync();
        File.WriteAllText(Path.Combine(_tmp.Ruta, "releases.json"), "{ dañado");

        ResultadoReleases r = await fuente.ObtenerAsync();

        Assert.Single(r.Releases);
        Assert.Empty(handler.Pedidos[2].Headers.IfNoneMatch);
    }

    [Fact]
    public async Task Un_304_sin_lista_guardada_no_entra_en_bucle()
    {
        var handler = new HandlerFalso(_ => Respuestas.Estado(HttpStatusCode.NotModified));

        var error = await Assert.ThrowsAsync<GestorException>(() => Fuente(handler).ObtenerAsync());

        Assert.Equal(TipoError.SinConexion, error.Tipo);
        Assert.Equal(2, handler.Pedidos.Count);
    }

    [Fact]
    public async Task Tiempo_agotado_usa_la_lista_guardada_y_sin_ella_lanza_SinConexion()
    {
        var sinCache = new HandlerFalso(_ => throw new TaskCanceledException("tiempo agotado"));
        var error = await Assert.ThrowsAsync<GestorException>(() => Fuente(sinCache).ObtenerAsync());
        Assert.Equal(TipoError.SinConexion, error.Tipo);
        Assert.Contains("a tiempo", error.Message);

        var conCache = PrimeroBienDespues(() => throw new TaskCanceledException("tiempo agotado"));
        FuenteReleases fuente = Fuente(conCache);
        await fuente.ObtenerAsync();
        ResultadoReleases r = await fuente.ObtenerAsync();
        Assert.Single(r.Releases);
        Assert.Contains("a tiempo", r.Aviso);
    }

    [Fact]
    public async Task Cancelar_no_se_confunde_con_sin_conexion()
    {
        var handler = new HandlerFalso(_ => Respuestas.Texto(Json));
        using var cancelado = new CancellationTokenSource();
        cancelado.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Fuente(handler).ObtenerAsync(cancelado.Token));
    }

    [Fact]
    public async Task Error_del_servidor_usa_la_lista_guardada_con_aviso()
    {
        var handler = PrimeroBienDespues(() => Respuestas.Estado(HttpStatusCode.BadGateway));
        FuenteReleases fuente = Fuente(handler);
        await fuente.ObtenerAsync();

        ResultadoReleases r = await fuente.ObtenerAsync();

        Assert.Single(r.Releases);
        Assert.Contains("502", r.Aviso);
    }

    [Fact]
    public async Task Repo_no_encontrado_sin_lista_guardada_dice_el_codigo()
    {
        // Lo que responde GitHub mientras el repo no sea público con el nombre CQEING-Addins.
        var handler = new HandlerFalso(_ => Respuestas.Estado(HttpStatusCode.NotFound));
        var error = await Assert.ThrowsAsync<GestorException>(() => Fuente(handler).ObtenerAsync());
        Assert.Equal(TipoError.SinConexion, error.Tipo);
        Assert.Contains("404", error.Message);
    }

    [Theory]
    [InlineData("{ no es json")]
    [InlineData("""{ "message": "Not Found" }""")]
    [InlineData("null")]
    [InlineData("""[{ "tag_name": "x", "draft": "quizá" }]""")]
    public async Task Respuesta_ilegible_sin_lista_guardada_lanza_error_controlado(string cuerpo)
    {
        var handler = new HandlerFalso(_ => Respuestas.Texto(cuerpo));
        var error = await Assert.ThrowsAsync<GestorException>(() => Fuente(handler).ObtenerAsync());
        Assert.Contains("no se pudo leer", error.Message);
        Assert.False(File.Exists(Path.Combine(_tmp.Ruta, "releases.json")));
    }

    [Fact]
    public async Task Respuesta_ilegible_con_lista_guardada_la_conserva_y_avisa()
    {
        var handler = PrimeroBienDespues(() => Respuestas.Texto("<html>error</html>", "\"v2\""));
        FuenteReleases fuente = Fuente(handler);
        await fuente.ObtenerAsync();

        ResultadoReleases r = await fuente.ObtenerAsync();

        Assert.Single(r.Releases);
        Assert.Contains("no se pudo leer", r.Aviso);
        Assert.Equal("\"v1\"", File.ReadAllText(Path.Combine(_tmp.Ruta, "releases.etag")));
    }

    [Fact]
    public async Task Releases_y_archivos_incompletos_se_descartan_sin_romper_la_lista()
    {
        const string incompleta = """
            [
              { "draft": false, "prerelease": false, "assets": [] },
              null,
              { "tag_name": "imagenes-v1.0.0", "draft": false, "prerelease": false },
              { "tag_name": "armado-columnas-v1.0.0", "draft": false, "prerelease": false, "assets": [
                  { "name": "manifest.json", "browser_download_url": "https://github.test/ok/manifest.json" },
                  { "name": "sin-url.zip" },
                  { "browser_download_url": "https://github.test/sin-nombre.zip" },
                  { "name": "relativa.zip", "browser_download_url": "descargas/relativa.zip" },
                  { "name": "insegura.zip", "browser_download_url": "http://github.test/insegura.zip" },
                  null
              ] }
            ]
            """;
        var handler = new HandlerFalso(_ => Respuestas.Texto(incompleta));

        ResultadoReleases r = await Fuente(handler).ObtenerAsync();

        Assert.Equal(new[] { "imagenes-v1.0.0", "armado-columnas-v1.0.0" }, r.Releases.Select(x => x.Etiqueta));
        Assert.Empty(r.Releases[0].Archivos);
        Assert.Equal("manifest.json", Assert.Single(r.Releases[1].Archivos).Nombre);
    }

    [Fact]
    public async Task Respuesta_sin_ETag_no_deja_un_ETag_viejo()
    {
        int llamada = 0;
        var handler = new HandlerFalso(_ => ++llamada == 1 ? Respuestas.Texto(Json, "\"v1\"") : Respuestas.Texto(Json));
        FuenteReleases fuente = Fuente(handler);
        await fuente.ObtenerAsync();

        await fuente.ObtenerAsync();
        await fuente.ObtenerAsync();

        Assert.False(File.Exists(Path.Combine(_tmp.Ruta, "releases.etag")));
        Assert.Empty(handler.Pedidos[2].Headers.IfNoneMatch);
    }

    private void EnvejecerListaGuardada(TimeSpan edad) =>
        File.SetLastWriteTimeUtc(Path.Combine(_tmp.Ruta, "releases.json"), DateTime.UtcNow - edad);

    [Fact]
    public async Task Lista_reciente_se_usa_sin_preguntar_a_GitHub()
    {
        var handler = PrimeroBienDespues(() => throw new HttpRequestException("no debía consultar"));
        FuenteReleases fuente = Fuente(handler);
        await fuente.ObtenerAsync();
        EnvejecerListaGuardada(TimeSpan.FromMinutes(10));

        ResultadoReleases r = await fuente.ObtenerRecienteAsync(TimeSpan.FromMinutes(30));

        Assert.Single(handler.Pedidos);
        Assert.False(r.DesdeCache);
        Assert.Equal("armado-columnas-v1.0.0", Assert.Single(r.Releases).Etiqueta);
    }

    [Fact]
    public async Task Lista_vieja_se_vuelve_a_consultar()
    {
        var handler = new HandlerFalso(_ => Respuestas.Texto(Json, "\"v1\""));
        FuenteReleases fuente = Fuente(handler);
        await fuente.ObtenerAsync();
        EnvejecerListaGuardada(TimeSpan.FromMinutes(40));

        await fuente.ObtenerRecienteAsync(TimeSpan.FromMinutes(30));

        Assert.Equal(2, handler.Pedidos.Count);
    }

    [Fact]
    public async Task Sin_lista_guardada_se_consulta()
    {
        var handler = new HandlerFalso(_ => Respuestas.Texto(Json, "\"v1\""));

        ResultadoReleases r = await Fuente(handler).ObtenerRecienteAsync(TimeSpan.FromMinutes(30));

        Assert.Single(handler.Pedidos);
        Assert.Single(r.Releases);
    }

    [Fact]
    public async Task Un_304_renueva_la_fecha_y_la_siguiente_apertura_no_consulta()
    {
        var handler = PrimeroBienDespues(() => Respuestas.Estado(HttpStatusCode.NotModified));
        FuenteReleases fuente = Fuente(handler);
        await fuente.ObtenerAsync();
        EnvejecerListaGuardada(TimeSpan.FromHours(2));

        await fuente.ObtenerRecienteAsync(TimeSpan.FromMinutes(30)); // 304
        await fuente.ObtenerRecienteAsync(TimeSpan.FromMinutes(30)); // reciente: no consulta

        Assert.Equal(2, handler.Pedidos.Count);
    }

    [Fact]
    public async Task Guardar_no_deja_temporales()
    {
        var handler = new HandlerFalso(_ => Respuestas.Texto(Json, "\"v1\""));
        FuenteReleases fuente = Fuente(handler);

        await fuente.ObtenerAsync();
        await fuente.ObtenerAsync();

        Assert.Empty(Directory.GetFiles(_tmp.Ruta, "*.tmp"));
    }
}
