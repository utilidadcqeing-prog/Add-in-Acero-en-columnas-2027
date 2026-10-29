using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CQEING.Gestor.Core.Tests.Apoyo;

/// <summary>Firma licencias como tools/licencias/servicio.gs, con una clave de prueba creada al vuelo.</summary>
internal static class FirmaPrueba
{
    public const string Codigo = "AAAA-BBBB-CCCC-DDDD";

    public static readonly RSA Clave = RSA.Create(2048);
    public static readonly string Publica = Clave.ExportSubjectPublicKeyInfoPem();

    /// <summary>El texto en UTF-8, RSA PKCS#1 v1.5 con SHA-256, en base64.</summary>
    public static string Firmar(string datos, RSA? clave = null) =>
        Convert.ToBase64String((clave ?? Clave).SignData(Encoding.UTF8.GetBytes(datos), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

    /// <summary>Como Date.toISOString(): con milisegundos y «Z».</summary>
    public static string Iso(DateTime fecha) => fecha.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static string Datos(DateTime emitida, DateTime vence, string codigo = Codigo, string nombre = "Santiago Bula", string prefijo = "cqeing-licencia-v1") =>
        $"{prefijo}|{codigo}|{nombre}|{Iso(emitida)}|{Iso(vence)}";

    /// <summary>El contenido de licencia.json (y del campo «licencia» de la respuesta del servicio).</summary>
    public static string Archivo(string datos, string? firma = null) => JsonSerializer.Serialize(new { datos, firma = firma ?? Firmar(datos) });

    public static string LicenciaFirmada(DateTime emitida, string codigo = Codigo, string nombre = "Santiago Bula") =>
        Archivo(Datos(emitida, emitida.AddDays(3), codigo, nombre));
}
