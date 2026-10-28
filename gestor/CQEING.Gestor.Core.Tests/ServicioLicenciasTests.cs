using System.Net;
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class ServicioLicenciasTests
{
    private static readonly DatosSolicitud Solicitud = new("AAAA-BBBB-CCCC-DDDD", "Yina Londoño", "PC-OBRA-02", "ylondono", "1.4.0");

    private static ServicioLicencias Servicio(HandlerFalso handler) => new(handler.Cliente(), "https://servicio.test/exec");

    private static Task<RespuestaServicio> Pedir(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        Servicio(new HandlerFalso(responder)).PedirAsync(Solicitud);

    [Fact]
    public async Task Respuesta_aprobado_devuelve_Aprobada_con_la_licencia()
    {
        RespuestaServicio r = await Pedir(_ => Respuestas.Texto("""{"estado":"aprobado","licencia":{"datos":"d","firma":"f"}}"""));

        Assert.Equal(EstadoSolicitud.Aprobada, r.Estado);
        Assert.Equal("""{"datos":"d","firma":"f"}""", r.ContenidoLicencia);
    }

    [Theory]
    [InlineData("""{"estado":"pendiente"}""", EstadoSolicitud.Pendiente)]
    [InlineData("""{"estado":"revocado"}""", EstadoSolicitud.Revocada)]
    [InlineData("""{"error":"codigo"}""", EstadoSolicitud.SinConexion)]
    [InlineData("""{"error":"servidor"}""", EstadoSolicitud.SinConexion)]
    [InlineData("""{"estado":"aprobado"}""", EstadoSolicitud.SinConexion)]
    [InlineData("""{"estado":"otro"}""", EstadoSolicitud.SinConexion)]
    [InlineData("<html>Google</html>", EstadoSolicitud.SinConexion)]
    [InlineData("[]", EstadoSolicitud.SinConexion)]
    public async Task Cada_respuesta_da_su_estado(string json, EstadoSolicitud esperado)
    {
        RespuestaServicio r = await Pedir(_ => Respuestas.Texto(json));

        Assert.Equal(esperado, r.Estado);
        Assert.Null(r.ContenidoLicencia);
    }

    [Fact]
    public async Task Error_500_devuelve_SinConexion()
    {
        Assert.Equal(EstadoSolicitud.SinConexion, (await Pedir(_ => Respuestas.Estado(HttpStatusCode.InternalServerError))).Estado);
    }

    [Fact]
    public async Task Sin_red_devuelve_SinConexion()
    {
        Assert.Equal(EstadoSolicitud.SinConexion, (await Pedir(_ => throw new HttpRequestException("sin red"))).Estado);
    }

    [Fact]
    public async Task Manda_los_cinco_datos_en_la_query()
    {
        var handler = new HandlerFalso(_ => Respuestas.Texto("""{"estado":"pendiente"}"""));

        await Servicio(handler).PedirAsync(Solicitud);

        HttpRequestMessage pedido = Assert.Single(handler.Pedidos);
        Assert.Equal(HttpMethod.Get, pedido.Method);
        Assert.Equal(
            "https://servicio.test/exec?codigo=AAAA-BBBB-CCCC-DDDD&nombre=Yina%20Londo%C3%B1o&equipo=PC-OBRA-02&usuarioWindows=ylondono&version=1.4.0",
            pedido.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public void La_url_por_defecto_es_la_del_script()
    {
        Assert.StartsWith("https://script.google.com/macros/s/", ServicioLicencias.Url);
        Assert.EndsWith("/exec", ServicioLicencias.Url);
    }
}
