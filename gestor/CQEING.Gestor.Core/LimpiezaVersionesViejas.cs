namespace CQEING.Gestor.Core;

/// <summary>
/// Deja en el equipo solo lo que se usa (pedido de Juan, 2026-10-29). Las versiones de antes de la licencia no la piden
/// ni obedecen la actualización obligatoria: se borran con su .addin y con los .addin de antes del gestor. De las demás
/// queda solo la activa: actualizar es obligatorio, así que una versión anterior ya no sirve. Una compilación de
/// desarrollo (.addin que apunta al repo) no se toca. Corre al abrir el gestor y al abrir Revit; nunca lanza.
/// Plan: docs/planes/2026-10-29-actualizacion-obligatoria.md, Decisión 11.
/// </summary>
public static class LimpiezaVersionesViejas
{
    /// <summary>La primera versión publicada con licencia de cada add-in (etiquetas del 2026-10-29).</summary>
    public static readonly IReadOnlyDictionary<string, VersionAddin> PrimeraConLicencia = new Dictionary<string, VersionAddin>
    {
        ["acero-vigas"] = new(1, 1, 0, null),
        ["armado-columnas"] = new(1, 5, 0, null),
        ["cuadro-cantidades"] = new(1, 9, 0, null),
        ["imagenes"] = new(1, 1, 0, null),
    };

    /// <summary>Los .addin de antes del gestor («heredados» de los addin.json).</summary>
    public static readonly IReadOnlyList<string> Heredados = ["practicacq.addin", "CQEING_CuadroCantidades.addin"];

    public static void Ejecutar(RutasGestor rutas)
    {
        try
        {
            foreach (AddinInstalado instalado in LectorEstado.Leer(rutas).Where(EsSinLicencia))
                BorrarSinFallar(rutas.ArchivoAddin(instalado.Id, instalado.Anio));

            if (Directory.Exists(rutas.CarpetaAddinsRevitRaiz))
                foreach (string carpetaAnio in Directory.GetDirectories(rutas.CarpetaAddinsRevitRaiz))
                    foreach (string nombre in Heredados)
                        BorrarSinFallar(Path.Combine(carpetaAnio, nombre));

            BorrarVersionesInactivas(rutas);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Lo que quedó se borra la próxima vez.
        }
    }

    private static bool EsSinLicencia(AddinInstalado instalado) =>
        PrimeraConLicencia.TryGetValue(instalado.Id, out VersionAddin? primera)
        && VersionAddin.Interpretar(instalado.Version) is { } version
        && version.CompareTo(primera) < 0;

    /// <summary>
    /// Borra cada carpeta de versión que ningún año tenga activa. Revit puede tener abierta una DLL: entonces Windows no
    /// deja renombrar la carpeta y queda para la próxima (Instalador.RetirarSinFallar).
    /// </summary>
    private static void BorrarVersionesInactivas(RutasGestor rutas)
    {
        if (!Directory.Exists(rutas.CarpetaAddins)) return;
        HashSet<(string Id, string Version)> activas = LectorEstado.Leer(rutas).Select(a => (a.Id, a.Version)).ToHashSet();

        foreach (string carpetaId in Directory.GetDirectories(rutas.CarpetaAddins))
        {
            string id = Path.GetFileName(carpetaId);
            foreach (string carpetaVersion in Directory.GetDirectories(carpetaId))
            {
                string version = Path.GetFileName(carpetaVersion);
                if (VersionAddin.Interpretar(version) is not null && !activas.Contains((id, version)))
                    Instalador.RetirarSinFallar(carpetaVersion);
            }
        }
    }

    private static void BorrarSinFallar(string archivo)
    {
        try
        {
            if (File.Exists(archivo)) File.Delete(archivo);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Queda para la próxima.
        }
    }
}
