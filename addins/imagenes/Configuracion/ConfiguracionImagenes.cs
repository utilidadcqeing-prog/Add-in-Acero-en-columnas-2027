using System.IO;

namespace practicacq.Imagenes.Configuracion;

/// <summary>
/// Lee la configuración del add-in de imágenes desde <c>%AppData%\CQEING\imagenes\.env</c>.
///
/// Por qué ahí y no junto al .dll (decisión del 2026-10-13, ver
/// <c>docs/planes/2026-10-13-addin-imagenes-por-actividad.md</c>): <c>practicacq.dll</c> se
/// instala directo en <c>Addins\{año}\</c>, una carpeta que comparten todos los add-ins de Revit, y
/// un <c>.env</c> suelto ahí puede pisarse con el de otro add-in. En AppData además sobrevive a
/// reinstalar o actualizar el add-in. Es propio de este add-in y no se comparte con el de
/// cantidades: los dos se actualizan por separado. Desde la issue #186 la cuenta de Cloudinary es la
/// de CQEING (<c>df6m0s0ri</c>) con subida firmada.
///
/// Si el archivo no existe, se crea con las claves vacías para que el usuario solo tenga que
/// completarlo.
/// </summary>
public sealed class ConfiguracionImagenes
{
    public static string Carpeta { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CQEING", "imagenes");

    public static string RutaEnv => Path.Combine(Carpeta, ".env");

    private const string Plantilla =
        "# Configuración del add-in de imágenes por actividad (CQEING).\n" +
        "# API de CQEING: la misma URL y X-Api-Key que usa el add-in de cantidades.\n" +
        "# Si quedan vacías, el add-in trabaja con datos de ejemplo.\n" +
        "API_URL=\n" +
        "API_KEY=\n" +
        "\n" +
        "# Cloudinary: cuenta de CQEING. Con API_KEY + API_SECRET se sube con firma (no hace falta preset)\n" +
        "# y al actualizar una imagen se borra la versión anterior. Sin ellas se usa el preset SIN firma.\n" +
        "# Nunca subir estos valores al repo.\n" +
        "CLOUDINARY_CLOUD_NAME=df6m0s0ri\n" +
        "CLOUDINARY_UPLOAD_PRESET=\n" +
        "CLOUDINARY_API_KEY=\n" +
        "CLOUDINARY_API_SECRET=\n";

    public string ApiUrl { get; private init; } = string.Empty;
    public string ApiKey { get; private init; } = string.Empty;
    public string CloudName { get; private init; } = string.Empty;
    public string UploadPreset { get; private init; } = string.Empty;
    public string CloudinaryApiKey { get; private init; } = string.Empty;
    public string CloudinaryApiSecret { get; private init; } = string.Empty;

    /// <summary>Sin URL o sin clave de la API se trabaja con datos de ejemplo.</summary>
    public bool ApiConfigurada => !string.IsNullOrWhiteSpace(ApiUrl) && !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// Con API Key + Secret se sube con firma y se puede borrar (issue #186, decisión 10 de
    /// <c>docs/planes/2026-10-14-actualizar-imagenes.md</c>).
    /// </summary>
    public bool CloudinaryFirmado =>
        !string.IsNullOrWhiteSpace(CloudName) && !string.IsNullOrWhiteSpace(CloudinaryApiKey) && !string.IsNullOrWhiteSpace(CloudinaryApiSecret);

    public bool CloudinaryConfigurado => CloudinaryFirmado || (!string.IsNullOrWhiteSpace(CloudName) && !string.IsNullOrWhiteSpace(UploadPreset));

    public static ConfiguracionImagenes Cargar()
    {
        Directory.CreateDirectory(Carpeta);
        if (!File.Exists(RutaEnv))
        {
            File.WriteAllText(RutaEnv, Plantilla);
        }

        var valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string linea in File.ReadAllLines(RutaEnv))
        {
            string texto = linea.Trim();
            if (texto.Length == 0 || texto.StartsWith('#')) continue;

            int igual = texto.IndexOf('=');
            if (igual <= 0) continue;

            string valor = texto[(igual + 1)..].Trim().Trim('"', '\'');
            valores[texto[..igual].Trim()] = valor;
        }

        string Leer(string clave) =>
            valores.TryGetValue(clave, out string? v) && !string.IsNullOrWhiteSpace(v)
                ? v
                : Environment.GetEnvironmentVariable(clave) ?? string.Empty;

        return new ConfiguracionImagenes
        {
            ApiUrl = Leer("API_URL").TrimEnd('/'),
            ApiKey = Leer("API_KEY"),
            CloudName = Leer("CLOUDINARY_CLOUD_NAME"),
            UploadPreset = Leer("CLOUDINARY_UPLOAD_PRESET"),
            CloudinaryApiKey = Leer("CLOUDINARY_API_KEY"),
            CloudinaryApiSecret = Leer("CLOUDINARY_API_SECRET"),
        };
    }
}
