using System.Net;
using System.Net.Http.Headers;

namespace CQEING.Gestor.Core.Tests.Apoyo;

internal static class Respuestas
{
    public static HttpResponseMessage Texto(string contenido, string? etag = null)
    {
        var respuesta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(contenido) };
        if (etag is not null) respuesta.Headers.ETag = new EntityTagHeaderValue(etag);
        return respuesta;
    }

    public static HttpResponseMessage Bytes(byte[] datos) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(datos) };

    public static HttpResponseMessage Estado(HttpStatusCode estado) => new(estado);
}
