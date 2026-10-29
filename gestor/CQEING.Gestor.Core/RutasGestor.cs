using System.Text.RegularExpressions;
using CQEING.Compartido;

namespace CQEING.Gestor.Core;

/// <summary>
/// Dónde vive cada cosa del gestor. Recibe las carpetas base para poder probarlo sin tocar las del usuario.
/// El id, la versión y el año terminan en carpetas que el gestor crea y BORRA, así que solo se aceptan con su
/// formato exacto (sin barras ni «..»); cualquier otro valor es un error de programación y lanza ArgumentException.
/// </summary>
public sealed class RutasGestor(string localAppData, string appData, string? claveRegistro = null)
{
    private static readonly Regex IdValido = new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex AnioValido = new(@"^[0-9]{4}$", RegexOptions.CultureInvariant);

    public static RutasGestor DelUsuario() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ActualizacionObligatoria.ClaveRegistro);

    public string CarpetaAddins => Path.Combine(localAppData, "CQEING", "Addins");

    /// <summary>
    /// Copia en el registro de las últimas versiones (ActualizacionObligatoria). Solo la del usuario real la tiene: las
    /// rutas de prueba no tocan el registro.
    /// </summary>
    public string? ClaveRegistro => claveRegistro;

    /// <summary>%LOCALAPPDATA%\CQEING: la licencia y las últimas versiones, lo que leen los add-ins al abrir Revit.</summary>
    public string CarpetaCqeing => Path.Combine(localAppData, "CQEING");

    public string CarpetaCache => Path.Combine(localAppData, "CQEING", "Gestor", "cache");

    /// <summary>
    /// Zona de trabajo de las descargas (ZIP y extracción). Está FUERA de la carpeta de versiones: si algo se corta,
    /// lo que queda aquí nunca se confunde con una versión instalada.
    /// </summary>
    public string CarpetaTemporal => Path.Combine(localAppData, "CQEING", "Gestor", "temp");

    public string CarpetaVersiones(string id) => Path.Combine(CarpetaAddins, ValidarId(id));

    /// <summary>Una carpeta por versión y año: instalar con Revit abierto no choca con la DLL que Revit tiene cargada.</summary>
    public string CarpetaVersion(string id, string version, string anio) =>
        Path.Combine(CarpetaVersiones(id), ValidarVersion(version), ValidarAnio(anio));

    /// <summary>
    /// Componente del gestor dentro de Revit (panel «Gestor»): una carpeta por versión del gestor y año de Revit,
    /// fuera de la carpeta de add-ins para que nunca se confunda con uno de ellos.
    /// </summary>
    public string CarpetaComplementoRevit => Path.Combine(localAppData, "CQEING", "Gestor", "revit");

    public string CarpetaComplemento(string versionGestor, string anio) =>
        Path.Combine(CarpetaComplementoRevit, ValidarVersion(versionGestor), ValidarAnio(anio));

    /// <summary>
    /// Revit carga los .addin en orden alfabético por nombre de archivo y cada add-in agrega su panel al final de la
    /// pestaña CQEING al cargar. El prefijo «zz-» hace que el panel «Gestor» se cree el último y quede a la derecha de
    /// todos los de los add-ins (lo vigila una prueba contra addins/*/addin.json).
    /// </summary>
    public const string NombreArchivoComplemento = ActualizacionObligatoria.ArchivoComplementoGestor;

    public string ArchivoComplemento(string anio) => Path.Combine(CarpetaAddinsRevit(anio), NombreArchivoComplemento);

    /// <summary>
    /// Qué modelo tiene activo cada Revit abierto: un archivo por proceso, lo escribe el componente del gestor dentro de
    /// Revit. El gestor lo lee para volver a abrir ese modelo después de reiniciar Revit (ReinicioRevit).
    /// </summary>
    public string CarpetaSesionesRevit => Path.Combine(localAppData, "CQEING", "Gestor", "sesiones-revit");

    public string ArchivoSesionRevit(int idProceso) => Path.Combine(CarpetaSesionesRevit, $"{idProceso}.txt");

    /// <summary>Revit que se cerraron para actualizar el gestor: el gestor nuevo los vuelve a abrir al arrancar.</summary>
    public string ArchivoReabrirRevit => Path.Combine(localAppData, "CQEING", "Gestor", "reabrir-revit.json");

    public string CarpetaAddinsRevitRaiz => Path.Combine(appData, "Autodesk", "Revit", "Addins");

    public string CarpetaAddinsRevit(string anio) => Path.Combine(CarpetaAddinsRevitRaiz, ValidarAnio(anio));

    public string ArchivoAddin(string id, string anio) => Path.Combine(CarpetaAddinsRevit(anio), $"CQEING.{ValidarId(id)}.addin");

    private static string ValidarId(string id) =>
        id is not null && IdValido.IsMatch(id) ? id : throw new ArgumentException($"«{id}» no es un id de add-in válido.", nameof(id));

    private static string ValidarAnio(string anio) =>
        anio is not null && AnioValido.IsMatch(anio) ? anio : throw new ArgumentException($"«{anio}» no es un año de Revit válido.", nameof(anio));

    private static string ValidarVersion(string version) =>
        VersionAddin.Interpretar(version) is not null ? version : throw new ArgumentException($"«{version}» no es una versión X.Y.Z válida.", nameof(version));
}
