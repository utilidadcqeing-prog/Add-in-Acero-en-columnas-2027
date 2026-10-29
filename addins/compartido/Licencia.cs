using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CQEING.Compartido;

internal enum ResultadoLicencia { Vigente, NoExiste, Invalida, OtroEquipo, Vencida, RelojAtrasado }

/// <summary>Resultado de comprobar la licencia. Nombre y fechas solo vienen si la firma era válida.</summary>
internal sealed record EstadoLicencia(ResultadoLicencia Resultado, string? Nombre = null, DateTime? EmitidaUtc = null, DateTime? VenceUtc = null)
{
    public bool EsVigente => Resultado == ResultadoLicencia.Vigente;

    /// <summary>Texto para el usuario. Termina con el código del equipo, que es lo que se aprueba en la hoja.</summary>
    public string Motivo(string? codigoEquipo)
    {
        string texto = Resultado switch
        {
            ResultadoLicencia.Vigente => $"Licencia vigente hasta el {VenceUtc?.ToLocalTime():dd-MM-yyyy}.",
            ResultadoLicencia.NoExiste => "Este equipo no tiene licencia de los add-ins de CQEING.",
            ResultadoLicencia.OtroEquipo => "La licencia guardada es de otro equipo.",
            ResultadoLicencia.Vencida => $"La licencia venció el {VenceUtc?.ToLocalTime():dd-MM-yyyy}.",
            ResultadoLicencia.RelojAtrasado => "La fecha de este equipo está atrasada. Corríjala y vuelva a abrir Revit.",
            _ => "La licencia de este equipo no es válida.",
        };
        return codigoEquipo is null
            ? $"{texto} No se pudo leer el código de este equipo."
            : $"{texto} Código del equipo: {codigoEquipo}";
    }
}

/// <summary>
/// Comprueba la licencia de los add-ins (issue #189). La firma la hoja «Licencias CQEING» (tools/licencias/servicio.gs)
/// con su clave privada; aquí solo está la pública, que sirve para comprobar pero no para firmar. Se compila dentro de cada
/// add-in y del núcleo del gestor. Reglas: docs/planes/2026-10-28-licencias-hoja-google.md, Decisiones 2, 3, 7 y 8.
/// </summary>
internal static class Licencia
{
    /// <summary>Pareja de la CLAVE_PRIVADA del script. Si se cambia la clave, cambia aquí y en la prueba (LEEME.md).</summary>
    public const string ClavePublica = """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAt37M3E8KM5jZtroAmNe2
        OUM25Br9CQcfpkqYQ++y2G6rjRphu39S5ECk3oIzha7TrOcpf5BJ0tuO6LWNrfc3
        pgQoCUEJtCwCkk5rL/4u52y+PWO07Jefzx6/A/D3489kor7HShFa2It5K23AQTCf
        7hHLtzhpUxeycNf4ceSDysse+Cm1siZ7DWw/UKF62VJaZebkmC/KAhbq1jat3kiC
        wHWGeymLJQDhVX1CzPRljnekXny7V+XJU4iE1714j8hQY6OSrPeqaxvrcy6NpZqe
        Bgbl1PzDrsf9gW6J/8v3N/tWSluPwADSmSGfhokyavyksq22JOKWjD8sSNFN6C0V
        0wIDAQAB
        -----END PUBLIC KEY-----
        """;

    public const string NombreArchivo = "licencia.json";
    public const string NombreArchivoVista = "licencia-vista.txt";

    private const string Prefijo = "cqeing-licencia-v1";

    /// <summary>Como escribe las fechas Date.toISOString() en servicio.gs.</summary>
    private const string FormatoFecha = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>Diferencia de reloj que se acepta entre este equipo y Google antes de dar el reloj por atrasado.</summary>
    private static readonly TimeSpan Tolerancia = TimeSpan.FromHours(1);

    /// <summary>%LOCALAPPDATA%\CQEING, junto a las carpetas del gestor.</summary>
    public static string CarpetaPorDefecto =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CQEING");

    /// <summary>
    /// Lo que usa cada add-in al abrir Revit. Nunca lanza: si algo falla (disco, registro, formato), no hay licencia,
    /// porque una excepción en OnStartup la muestra Revit como error del add-in.
    /// </summary>
    public static bool Vigente(out string motivo)
    {
        string? codigo = null;
        try
        {
            codigo = CodigoEquipo.Actual();
            EstadoLicencia estado = Leer(CarpetaPorDefecto, codigo, DateTime.UtcNow, ClavePublica);
            motivo = estado.Motivo(codigo);
            return estado.EsVigente;
        }
        catch (Exception)
        {
            motivo = new EstadoLicencia(ResultadoLicencia.Invalida).Motivo(codigo);
            return false;
        }
    }

    /// <summary>Lee licencia.json y la última fecha vista de <paramref name="carpeta"/> y las comprueba.</summary>
    public static EstadoLicencia Leer(string carpeta, string? codigoEquipo, DateTime ahoraUtc, string clavePublicaPem)
    {
        string? contenido;
        try
        {
            string archivo = Path.Combine(carpeta, NombreArchivo);
            contenido = File.Exists(archivo) ? File.ReadAllText(archivo) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new EstadoLicencia(ResultadoLicencia.Invalida);
        }
        return Verificar(contenido, codigoEquipo, ahoraUtc, UltimaVista(carpeta), clavePublicaPem);
    }

    /// <summary>
    /// Comprueba el contenido de licencia.json: {"datos":"cqeing-licencia-v1|código|nombre|emitida|vence","firma":"base64"}.
    /// Primero la firma; solo si es válida se cree lo que dicen los datos.
    /// </summary>
    public static EstadoLicencia Verificar(string? contenido, string? codigoEquipo, DateTime ahoraUtc, DateTime? ultimaVistaUtc, string clavePublicaPem)
    {
        if (contenido is null) return new EstadoLicencia(ResultadoLicencia.NoExiste);
        if (codigoEquipo is null
            || !Separar(contenido, out string datos, out byte[] firma)
            || !FirmaValida(datos, firma, clavePublicaPem))
            return new EstadoLicencia(ResultadoLicencia.Invalida);

        string[] partes = datos.Split('|');
        if (partes.Length != 5
            || partes[0] != Prefijo
            || !LeerFecha(partes[3], out DateTime emitida)
            || !LeerFecha(partes[4], out DateTime vence))
            return new EstadoLicencia(ResultadoLicencia.Invalida);

        var estado = new EstadoLicencia(ResultadoLicencia.Vigente, partes[2], emitida, vence);
        if (partes[1] != codigoEquipo) return estado with { Resultado = ResultadoLicencia.OtroEquipo };
        if (ahoraUtc < emitida - Tolerancia || (ultimaVistaUtc is { } vista && ahoraUtc < vista - Tolerancia))
            return estado with { Resultado = ResultadoLicencia.RelojAtrasado };
        if (ahoraUtc >= vence) return estado with { Resultado = ResultadoLicencia.Vencida };
        return estado;
    }

    /// <summary>La fecha más reciente que vio este equipo, o null si no hay o no se entiende.</summary>
    public static DateTime? UltimaVista(string carpeta)
    {
        try
        {
            string archivo = Path.Combine(carpeta, NombreArchivoVista);
            return File.Exists(archivo)
                && DateTime.TryParse(File.ReadAllText(archivo).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime vista)
                && vista.Kind == DateTimeKind.Utc
                    ? vista
                    : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Anota <paramref name="ahoraUtc"/> si es más reciente que lo anotado: así atrasar el reloj no revive una licencia
    /// vencida (Decisión 8). Es una ayuda: si no se puede escribir, no pasa nada.
    /// </summary>
    public static void AnotarVista(string carpeta, DateTime ahoraUtc)
    {
        if (UltimaVista(carpeta) is { } vista && vista >= ahoraUtc) return;
        try
        {
            Directory.CreateDirectory(carpeta);
            File.WriteAllText(Path.Combine(carpeta, NombreArchivoVista), ahoraUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // La próxima apertura lo vuelve a intentar.
        }
    }

    private static bool Separar(string contenido, out string datos, out byte[] firma)
    {
        datos = string.Empty;
        firma = [];
        try
        {
            using JsonDocument json = JsonDocument.Parse(contenido);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("datos", out JsonElement d) || d.ValueKind != JsonValueKind.String
                || !json.RootElement.TryGetProperty("firma", out JsonElement f) || f.ValueKind != JsonValueKind.String)
                return false;
            datos = d.GetString()!;
            firma = Convert.FromBase64String(f.GetString()!);
            return true;
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            return false;
        }
    }

    /// <summary>RSA PKCS#1 v1.5 con SHA-256 sobre el texto en UTF-8: lo mismo que computeRsaSha256Signature en servicio.gs.</summary>
    private static bool FirmaValida(string datos, byte[] firma, string clavePublicaPem)
    {
        try
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(clavePublicaPem);
            return rsa.VerifyData(Encoding.UTF8.GetBytes(datos), firma, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception e) when (e is ArgumentException or CryptographicException)
        {
            return false;
        }
    }

    private static bool LeerFecha(string texto, out DateTime fecha) =>
        DateTime.TryParseExact(texto, FormatoFecha, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out fecha);
}
