namespace CQEING.Gestor.Core.Tests.Apoyo;

/// <summary>Carpeta de prueba que se borra al terminar.</summary>
internal sealed class CarpetaTemporal : IDisposable
{
    public string Ruta { get; } = Directory.CreateTempSubdirectory("cqeing-pruebas-").FullName;

    public string Combinar(params string[] partes) => Path.Combine([Ruta, .. partes]);

    public void Dispose()
    {
        try { Directory.Delete(Ruta, recursive: true); }
        catch (IOException) { /* algún archivo sigue abierto; queda en %TEMP% */ }
    }
}
