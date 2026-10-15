namespace CQEING.Publicador.Tests;

internal sealed class CarpetaTemporal : IDisposable
{
    public string Ruta { get; } = Directory.CreateTempSubdirectory("cqeing-publicador-").FullName;

    public string Combinar(params string[] partes) => Path.Combine([Ruta, .. partes]);

    public void Dispose()
    {
        try { Directory.Delete(Ruta, recursive: true); }
        catch (IOException) { }
    }
}
