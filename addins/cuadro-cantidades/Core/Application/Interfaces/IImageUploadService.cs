using System.Threading.Tasks;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

/// <summary>
/// Define el servicio para subir imágenes a un almacenamiento en la nube y obtener su URL pública.
/// </summary>
public interface IImageUploadService
{
    /// <summary>
    /// Sube una imagen local a la nube y devuelve su dirección URL pública.
    /// </summary>
    /// <param name="filePath">Ruta local del archivo de imagen (PNG, JPG, etc.).</param>
    /// <returns>La URL pública de la imagen si se subió con éxito; de lo contrario, null o vacío.</returns>
    Task<string?> UploadImageAsync(string filePath);

    /// <summary>
    /// Sube una imagen en memoria a la nube y devuelve su dirección URL pública.
    /// Se usa cuando el archivo local ya no existe pero la imagen se conserva en Base64.
    /// </summary>
    /// <param name="imageData">Bytes de la imagen.</param>
    /// <param name="fileName">Nombre de archivo con el que se identifica la imagen.</param>
    /// <param name="contentType">Tipo MIME de la imagen (p. ej. "image/png").</param>
    /// <returns>La URL pública de la imagen si se subió con éxito; de lo contrario, null o vacío.</returns>
    Task<string?> UploadImageAsync(byte[] imageData, string fileName, string contentType);
}
