using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace CQEING.Gestor.Core.Tests.Apoyo;

internal static partial class Datos
{
    /// <summary>ZIP como el que arma el publicador: la DLL, Resources/ y .addin con la DLL sin carpeta.</summary>
    /// <param name="entradaExtra">Nombre de una entrada adicional (por ejemplo «../fuera.txt» para probar rutas peligrosas).</param>
    /// <param name="xml">Contenido del .addin, para probar uno dañado.</param>
    /// <param name="incluirDll">false: el .addin nombra una DLL que el paquete no trae.</param>
    public static byte[] ZipAddin(
        string dll = "practicacq.dll", int addins = 1, string? assembly = null,
        string? entradaExtra = null, string? xml = null, bool incluirDll = true)
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (incluirDll) Escribir(zip, dll, "contenido de la dll");
            Escribir(zip, "Resources/icono16.png", "png");
            for (int i = 0; i < addins; i++)
                Escribir(zip, i == 0 ? "practicacq.addin" : $"otro{i}.addin", xml ?? XmlAddin(assembly ?? dll));
            if (entradaExtra is not null) Escribir(zip, entradaExtra, "no debería salir del paquete");
        }
        return memoria.ToArray();
    }

    public static string XmlAddin(string assembly) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <RevitAddIns>
          <AddIn Type="Application">
            <Name>practicacq</Name>
            <Assembly>{assembly}</Assembly>
            <FullClassName>practicacq.App</FullClassName>
            <ClientId>2044e120-cc19-42a1-8018-a97f13adf192</ClientId>
            <VendorId>ADSK</VendorId>
          </AddIn>
        </RevitAddIns>
        """;

    public static string Sha256(byte[] datos) => Convert.ToHexString(SHA256.HashData(datos)).ToLowerInvariant();

    /// <summary>Versión publicada de armado-columnas con un paquete para <paramref name="anio"/> y la URL de su ZIP.</summary>
    public static (VersionPublicada Version, string Url, byte[] Zip) VersionConZip(
        string version, string anio = "2026", byte[]? zip = null, IReadOnlyList<string>? heredados = null)
    {
        zip ??= ZipAddin();
        PaqueteManifiesto paquete = Paquete(anio, Sha256(zip), version: version);
        string url = $"https://github.test/descargas/{paquete.Archivo}";
        var publicada = new VersionPublicada(
            Manifiesto(version: version, paquetes: [paquete], heredados: heredados),
            VersionAddin.Interpretar(version)!,
            new Dictionary<string, Uri> { [paquete.Archivo] = new Uri(url) });
        return (publicada, url, zip);
    }

    private static void Escribir(ZipArchive zip, string nombre, string contenido)
    {
        using Stream destino = zip.CreateEntry(nombre).Open();
        destino.Write(Encoding.UTF8.GetBytes(contenido));
    }
}
