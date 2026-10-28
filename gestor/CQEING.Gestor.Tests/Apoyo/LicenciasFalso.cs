using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CQEING.Gestor.Tests.Apoyo;

/// <summary>
/// La hoja «Licencias CQEING» simulada, sin red: responde el JSON que pida cada prueba y firma las licencias como
/// tools/licencias/servicio.gs, con una clave de prueba creada al vuelo (la misma estrategia que FirmaPrueba del núcleo).
/// </summary>
internal sealed class LicenciasFalso : HttpMessageHandler
{
    public const string Url = "https://servicio.test/exec";
    public const string Codigo = "AAAA-BBBB-CCCC-DDDD";

    private static readonly RSA Clave = RSA.Create(2048);
    public static readonly string Publica = Clave.ExportSubjectPublicKeyInfoPem();

    private string _respuesta = """{"estado":"pendiente"}""";
    private bool _sinRed;

    public int Pedidos { get; private set; }

    public void Pendiente() => Responder("""{"estado":"pendiente"}""");

    public void Revocado() => Responder("""{"estado":"revocado"}""");

    public void Aprobado(DateTime emitidaUtc, string nombre) => Responder($$"""{"estado":"aprobado","licencia":{{Licencia(emitidaUtc, nombre)}}}""");

    public void SinRed() => _sinRed = true;

    /// <summary>El contenido de licencia.json: {"datos","firma"} con vigencia de 3 días (Decisión 4).</summary>
    public static string Licencia(DateTime emitidaUtc, string nombre)
    {
        string datos = $"cqeing-licencia-v1|{Codigo}|{nombre}|{Iso(emitidaUtc)}|{Iso(emitidaUtc.AddDays(3))}";
        string firma = Convert.ToBase64String(Clave.SignData(Encoding.UTF8.GetBytes(datos), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return JsonSerializer.Serialize(new { datos, firma });
    }

    private void Responder(string json)
    {
        _respuesta = json;
        _sinRed = false;
    }

    /// <summary>Como Date.toISOString(): con milisegundos y «Z».</summary>
    private static string Iso(DateTime fecha) => fecha.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
    {
        Pedidos++;
        if (_sinRed) throw new HttpRequestException("sin red");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_respuesta) });
    }
}
