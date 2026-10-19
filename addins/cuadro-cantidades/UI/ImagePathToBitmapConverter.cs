using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.UI;

/// <summary>
/// Conversor para WPF que transforma la ruta de una imagen o un objeto <see cref="ActivityImageSnapshot"/>
/// en un objeto visualizable de tipo <see cref="ImageSource"/> (específicamente <see cref="BitmapFrame"/>).
/// </summary>
public class ImagePathToBitmapConverter : IValueConverter
{
    /// <summary>
    /// Convierte una ruta de archivo o una captura de imagen en un objeto BitmapFrame.
    /// </summary>
    /// <param name="value">El valor de origen, que puede ser un objeto ActivityImageSnapshot o una cadena con la ruta del archivo.</param>
    /// <param name="targetType">El tipo del destino del enlace (no se utiliza).</param>
    /// <param name="parameter">Parámetro opcional para el enlace (no se utiliza).</param>
    /// <param name="culture">La cultura que se utilizará en el convertidor (no se utiliza).</param>
    /// <returns>Un objeto BitmapFrame congelado listo para su uso en la interfaz, o null en caso de error o si la imagen no existe.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Si el valor recibido es una captura de imagen (ActivityImageSnapshot)
        if (value is ActivityImageSnapshot snapshot)
        {
            // Intentar cargar primero la imagen desde su cadena Base64 almacenada en la base de datos
            if (!string.IsNullOrWhiteSpace(snapshot.ImageDataBase64))
            {
                try
                {
                    byte[] imageBytes = System.Convert.FromBase64String(snapshot.ImageDataBase64);
                    using var stream = new MemoryStream(imageBytes);
                    var frame = BitmapFrame.Create(
                        stream,
                        BitmapCreateOptions.IgnoreImageCache
                            | BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad
                    );
                    frame.Freeze(); // Congelar para permitir su uso en hilos de UI diferentes
                    return frame;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ImagePathToBitmapConverter] Error loading DB image: {ex.Message}"
                    );
                }
            }

            // Si no hay datos Base64, usar la ruta del sistema de archivos (dando prioridad a la ruta de depuración)
            value = !string.IsNullOrWhiteSpace(snapshot.DebugImagePath)
                ? snapshot.DebugImagePath
                : snapshot.ImagePath;
        }

        // Si el valor es una cadena (ruta de archivo)
        if (value is string rawPath)
        {
            var path = rawPath.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (
                path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            )
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bitmap.EndInit();
                    if (bitmap.CanFreeze)
                    {
                        bitmap.Freeze();
                    }
                    return bitmap;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ImagePathToBitmapConverter] Error loading image from URL '{path}': {ex}"
                    );
                    return null;
                }
            }

            if (!File.Exists(path))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ImagePathToBitmapConverter] Image file not found: {path}"
                );
                return null;
            }

            try
            {
                // Abrir el archivo con uso compartido para lectura/escritura y evitar bloqueos en el disco
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite
                );
                var frame = BitmapFrame.Create(
                    stream,
                    BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad
                );
                frame.Freeze(); // Congelar el recurso para evitar fugas y problemas de subprocesos
                return frame;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ImagePathToBitmapConverter] Error loading image '{path}': {ex}"
                );
            }
        }
        return null;
    }

    /// <summary>
    /// No implementado. La conversión bidireccional no es compatible.
    /// </summary>
    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        throw new NotImplementedException();
    }
}
