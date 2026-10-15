namespace CQEING.Gestor.Core.Tests.Apoyo;

/// <summary>Servidor HTTP falso: responde con la función dada y guarda cada pedido.</summary>
internal sealed class HandlerFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<HttpRequestMessage> Pedidos { get; } = [];

    public HttpClient Cliente() => ClienteHttp.Crear(this);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
    {
        Pedidos.Add(pedido);
        return Task.FromResult(responder(pedido));
    }
}
