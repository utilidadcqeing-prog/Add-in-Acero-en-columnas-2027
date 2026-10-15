using System.Security.Cryptography;

namespace CQEING.Gestor.Core;

public static class Sha256Archivo
{
    public static string Calcular(string ruta)
    {
        using FileStream archivo = File.OpenRead(ruta);
        return Convert.ToHexString(SHA256.HashData(archivo)).ToLowerInvariant();
    }
}
