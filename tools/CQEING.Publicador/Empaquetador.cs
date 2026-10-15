using System.IO.Compression;

namespace CQEING.Publicador;

public static class Empaquetador
{
    /// <summary>Lo escribe addins/Directory.Build.props (target EscribirFramework); no va dentro del ZIP.</summary>
    public const string ArchivoFramework = "framework.txt";

    /// <summary>ZIP con la salida de compilación sin .pdb. Debe traer exactamente un .addin: el gestor lo activa.</summary>
    public static void CrearZip(string carpetaCompilada, string zipDestino)
    {
        int addins = Directory.GetFiles(carpetaCompilada, "*.addin").Length;
        if (addins != 1)
            throw new InvalidOperationException($"{carpetaCompilada} debe tener exactamente un .addin y tiene {addins}.");

        File.Delete(zipDestino);
        using ZipArchive zip = ZipFile.Open(zipDestino, ZipArchiveMode.Create);
        foreach (string archivo in Directory.EnumerateFiles(carpetaCompilada, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relativa = Path.GetRelativePath(carpetaCompilada, archivo);
            if (relativa == ArchivoFramework || Path.GetExtension(archivo).Equals(".pdb", StringComparison.OrdinalIgnoreCase))
                continue;
            zip.CreateEntryFromFile(archivo, relativa.Replace('\\', '/'), CompressionLevel.Optimal);
        }
    }
}
