namespace CQEING.Gestor.Core;

/// <param name="Anios">Años de Revit en los que el componente quedó registrado.</param>
/// <param name="Problemas">Años que no se pudieron registrar y por qué (se reintenta la próxima vez que se abre el gestor).</param>
public sealed record ResultadoRegistroComplemento(IReadOnlyList<string> Anios, IReadOnlyList<string> Problemas);

/// <summary>
/// Deja el componente del gestor (panel «Gestor» con su botón) listo en cada Revit instalado. El paquete del gestor
/// trae una carpeta por año de Revit (<c>revit\2026</c>, <c>revit\2027</c>): se copia a una carpeta propia por versión
/// del gestor y se escribe su .addin con ruta absoluta, igual que un add-in. Con Revit abierto es seguro: la versión
/// nueva va a otra carpeta y Revit la carga al abrirse de nuevo. No es un add-in del catálogo: nunca aparece como tarjeta.
/// </summary>
public sealed class RegistroComplementoRevit(RutasGestor rutas)
{
    public const int VersionesConservadas = 2;

    /// <param name="carpetaPaquete">Carpeta del gestor instalado que trae una subcarpeta por año de Revit.</param>
    /// <param name="versionGestor">Versión X.Y.Z del gestor que trae este componente.</param>
    /// <param name="aniosRevit">Años de Revit instalados en este equipo.</param>
    public ResultadoRegistroComplemento Registrar(string carpetaPaquete, string versionGestor, IEnumerable<string> aniosRevit)
    {
        var registrados = new List<string>();
        var problemas = new List<string>();
        foreach (string anio in aniosRevit)
        {
            string origen = Path.Combine(carpetaPaquete, anio);
            if (!Directory.Exists(origen)) continue; // este gestor no trae el componente para ese año

            try
            {
                string destino = rutas.CarpetaComplemento(versionGestor, anio);
                if (!Directory.Exists(destino) || !Instalador.TieneAddinValido(destino))
                    CopiarEntera(origen, destino);
                Instalador.ActivarAddin(destino, rutas.ArchivoComplemento(anio));
                registrados.Add(anio);
            }
            catch (Exception e) when (e is GestorException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                problemas.Add($"Revit {anio}: {e.Message}");
            }
        }

        if (registrados.Count > 0) LimpiarVersionesViejas(versionGestor);
        return new ResultadoRegistroComplemento(registrados, problemas);
    }

    /// <summary>
    /// Al desinstalar el gestor: quita el .addin del componente de cada año de Revit y su carpeta. Solo toca el
    /// archivo del componente; los add-ins de los usuarios y sus carpetas no se tocan. Con Revit abierto la carpeta
    /// puede no borrarse: queda inofensiva y el botón avisa que el gestor ya no está.
    /// </summary>
    public void Quitar()
    {
        if (Directory.Exists(rutas.CarpetaAddinsRevitRaiz))
        {
            foreach (string carpetaAnio in Directory.GetDirectories(rutas.CarpetaAddinsRevitRaiz))
            {
                try
                {
                    File.Delete(Path.Combine(carpetaAnio, RutasGestor.NombreArchivoComplemento));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Revit lo tiene abierto: se quita la próxima vez que se pueda.
                }
            }
        }

        if (Directory.Exists(rutas.CarpetaComplementoRevit))
            Instalador.RetirarSinFallar(rutas.CarpetaComplementoRevit);
    }

    /// <summary>Copia a una carpeta de trabajo y solo si salió todo bien la mueve a su lugar: nunca queda una versión a medias.</summary>
    private void CopiarEntera(string origen, string destino)
    {
        string trabajo = Path.Combine(rutas.CarpetaTemporal, Guid.NewGuid().ToString("N"));
        try
        {
            Copiar(origen, trabajo);
            if (Directory.Exists(destino)) Instalador.RetirarSinFallar(destino); // quedó dañada: se aparta
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            Directory.Move(trabajo, destino);
        }
        finally
        {
            if (Directory.Exists(trabajo)) Instalador.RetirarSinFallar(trabajo);
        }
    }

    private static void Copiar(string origen, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (string archivo in Directory.GetFiles(origen))
            File.Copy(archivo, Path.Combine(destino, Path.GetFileName(archivo)));
        foreach (string carpeta in Directory.GetDirectories(origen))
            Copiar(carpeta, Path.Combine(destino, Path.GetFileName(carpeta)));
    }

    /// <summary>Deja la versión actual y la anterior; las demás se retiran. Si Revit tiene una abierta, queda para la próxima vez.</summary>
    private void LimpiarVersionesViejas(string versionActual)
    {
        string raiz = rutas.CarpetaComplementoRevit;
        if (!Directory.Exists(raiz)) return;

        IEnumerable<string> sobrantes = Directory.GetDirectories(raiz)
            .Select(carpeta => (Carpeta: carpeta, Nombre: Path.GetFileName(carpeta), Version: VersionAddin.Interpretar(Path.GetFileName(carpeta))))
            .Where(x => x.Version is not null && x.Nombre != versionActual)
            .OrderByDescending(x => x.Version)
            .Skip(VersionesConservadas - 1)
            .Select(x => x.Carpeta);

        foreach (string carpeta in sobrantes)
            Instalador.RetirarSinFallar(carpeta);

        foreach (string resto in Directory.GetDirectories(raiz, "*.borrar-*"))
            Instalador.RetirarSinFallar(resto);
    }
}
