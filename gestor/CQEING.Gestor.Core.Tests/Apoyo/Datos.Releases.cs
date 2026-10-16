namespace CQEING.Gestor.Core.Tests.Apoyo;

internal static partial class Datos
{
    public static ReleaseGitHub Release(string etiqueta, bool prerelease = false, bool borrador = false, bool conManifiesto = true) =>
        new(etiqueta, borrador, prerelease,
            conManifiesto ? [new ArchivoRelease("manifest.json", new Uri($"https://github.test/{etiqueta}/manifest.json"))] : []);

    /// <summary>Sirve el manifest.json de cada etiqueta, con el id y la versión que dice la etiqueta.</summary>
    public static HttpResponseMessage ManifiestoDe(HttpRequestMessage pedido)
    {
        string etiqueta = pedido.RequestUri!.Segments[1].TrimEnd('/');
        EtiquetaRelease e = EtiquetaRelease.Interpretar(etiqueta)!;
        return Respuestas.Texto(SerializadorManifiesto.Escribir(Manifiesto(e.Id, e.Version.ToString())));
    }
}
