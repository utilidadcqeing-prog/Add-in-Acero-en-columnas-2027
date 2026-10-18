namespace CQEING.Gestor.Core;

public static class ClienteHttp
{
    public const string UserAgent = "CQEING-Gestor/1.0";

    /// <summary>
    /// Tiempo máximo de cada pedido. Alcanza para descargar un ZIP con una conexión lenta; si se pasa, el pedido
    /// termina como «sin conexión» (FuenteReleases usa la lista guardada) en vez de colgar la ventana.
    /// </summary>
    public static readonly TimeSpan TiempoMaximo = TimeSpan.FromMinutes(5);

    /// <summary>GitHub rechaza pedidos sin User-Agent. El handler solo se pasa en las pruebas.</summary>
    public static HttpClient Crear(HttpMessageHandler? handler = null)
    {
        HttpClient http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TiempoMaximo;
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return http;
    }
}
