using System.IO;
using System.Text.Json;
using practicacq.Imagenes.Configuracion;
using practicacq.Imagenes.Modelo;

namespace practicacq.Imagenes.Almacen;

/// <summary>
/// Copia local de las imágenes, una carpeta por presupuesto en
/// <c>%AppData%\CQEING\imagenes\{presupuesto}\</c> con un <c>registro.json</c> y los PNG.
/// Alimenta el tablero y permite trabajar sin conexión: si la subida a Cloudinary falla, la imagen
/// queda guardada aquí y se reintenta después.
///
/// Por qué JSON y no SQLite, como proponía la issue (decisión del 2026-10-13, ver
/// <c>docs/planes/2026-10-13-addin-imagenes-por-actividad.md</c>):
/// - El add-in de cantidades carga <c>Microsoft.EntityFrameworkCore.Sqlite 8.0.10</c> (con su
///   <c>e_sqlite3</c> nativo) en el mismo proceso de Revit. Si este add-in trajera otra versión, la
///   que cargue primero gana y la otra puede fallar al arrancar, según el orden en que Revit cargue
///   los add-ins: un error muy difícil de diagnosticar.
/// - Son decenas de imágenes por presupuesto y no hace falta consultar con SQL; el JSON se lee
///   entero en milisegundos y se puede abrir a mano para revisar.
/// Si más adelante hace falta una base de datos, solo hay que cambiar esta clase.
/// </summary>
public sealed class RegistroLocal
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    private readonly string _carpeta;
    private readonly string _rutaJson;
    private List<ImagenActividad> _imagenes;

    public RegistroLocal(string codigoPresupuesto)
    {
        _carpeta = Path.Combine(ConfiguracionImagenes.Carpeta, NombreSeguro(codigoPresupuesto));
        _rutaJson = Path.Combine(_carpeta, "registro.json");
        _imagenes = Leer();
    }

    public IReadOnlyList<ImagenActividad> Todas => _imagenes;

    public IEnumerable<ImagenActividad> DeActividad(string codigoActividad) =>
        _imagenes.Where(i => i.CodigoActividad == codigoActividad).OrderBy(i => i.Codigo, StringComparer.Ordinal);

    public int CantidadDe(string codigoActividad) => _imagenes.Count(i => i.CodigoActividad == codigoActividad);

    /// <summary>Ruta (sin extensión) donde Revit debe exportar el PNG de una imagen nueva.</summary>
    public string RutaParaImagen(string codigoImagen)
    {
        Directory.CreateDirectory(_carpeta);
        return Path.Combine(_carpeta, NombreSeguro(codigoImagen));
    }

    public void Agregar(ImagenActividad imagen)
    {
        _imagenes.RemoveAll(i => i.Codigo == imagen.Codigo);
        _imagenes.Add(imagen);
        Guardar();
    }

    public void Guardar()
    {
        Directory.CreateDirectory(_carpeta);
        // Escritura en dos pasos: si Revit se cierra a mitad, el registro anterior queda entero.
        string temporal = _rutaJson + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(_imagenes, _json));
        File.Move(temporal, _rutaJson, overwrite: true);
    }

    private List<ImagenActividad> Leer()
    {
        if (!File.Exists(_rutaJson)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<ImagenActividad>>(File.ReadAllText(_rutaJson), _json) ?? new();
        }
        catch (JsonException)
        {
            // Un registro roto no debe impedir abrir el add-in: se aparta y se empieza de cero.
            File.Move(_rutaJson, _rutaJson + $".roto-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
            return new();
        }
    }

    private static string NombreSeguro(string texto) =>
        string.Concat(texto.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
