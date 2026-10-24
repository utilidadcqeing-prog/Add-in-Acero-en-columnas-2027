namespace CQEING.Gestor.Core;

/// <summary>Un add-in instalado en un Revit que tiene una versión más nueva publicada para ese Revit.</summary>
public sealed record Novedad(string Id, string Nombre, string Instalada, string Nueva);

/// <summary>
/// Qué avisar dentro de Revit (panel «Gestor»): solo los add-ins que el usuario ya tiene instalados en ese año de Revit
/// y que tienen una versión más nueva con paquete para ese año. Lo que no está instalado no se avisa: es un aviso de
/// actualizaciones, no un anuncio del catálogo.
/// </summary>
public static class NovedadesRevit
{
    public static IReadOnlyList<Novedad> De(IEnumerable<EstadoAddin> estados, string anio) =>
        estados
            .Where(e => e.HayActualizacion(anio))
            .Select(e => new Novedad(e.Addin.Id, e.Addin.Nombre, e.Instalada(anio)!, e.Addin.Ultima.Version.ToString()))
            .ToList();
}
