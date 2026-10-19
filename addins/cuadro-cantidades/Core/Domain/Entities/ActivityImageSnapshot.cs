using System;
using System.Text.Json.Serialization;

namespace CQEING_CuadroCantidades.Core.Domain.Entities;

/// <summary>
/// Representa una captura instantánea (snapshot) de imagen asociada a una actividad.
/// Contiene la representación en base64 de la imagen y metadatos de la captura en Revit.
/// </summary>
public sealed class ActivityImageSnapshot
{
    /// <summary>
    /// Datos de la imagen codificados en formato Base64.
    /// Es la fuente principal para renderizar la imagen en la UI.
    /// </summary>
    public string? ImageDataBase64 { get; set; }

    /// <summary>
    /// Tipo de contenido MIME de la imagen. Por defecto es "image/png".
    /// </summary>
    public string ContentType { get; set; } = "image/png";

    /// <summary>
    /// Ruta de archivo local utilizada para depurar la imagen capturada.
    /// </summary>
    public string? DebugImagePath { get; set; }

    /// <summary>
    /// Ruta heredada o de depuración para la imagen.
    /// No debe usarse como la fuente primaria de la imagen.
    /// </summary>
    public string ImagePath { get; set; } = string.Empty;

    /// <summary>
    /// Fecha y hora en la que se realizó la captura de la pantalla en Revit.
    /// </summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>
    /// Lista de IDs de elementos Revit seleccionados o visibles en la captura, representados en formato de texto.
    /// </summary>
    public string ElementIds { get; set; } = string.Empty;

    /// <summary>
    /// Indica si esta imagen ha sido explícitamente seleccionada para publicación.
    /// </summary>
    public bool IsSelectedForPublication { get; set; } = false;

    /// <summary>
    /// URL pública de la imagen una vez subida a la nube.
    /// Si existe, la imagen ya está publicada y no se vuelve a subir.
    /// </summary>
    public string? PublishedUrl { get; set; }

    /// <summary>
    /// Código de la imagen en el add-in de imágenes (p. ej. "ACT_006_01"). Null en las capturas propias.
    /// Es estable: identifica la imagen aunque su URL cambie al actualizarla (…_v2.png).
    /// </summary>
    public string? AddinImageCode { get; set; }

    /// <summary>
    /// Nombre que se le dio a la imagen en el add-in de imágenes. Null en las capturas propias.
    /// </summary>
    public string? AddinImageName { get; set; }

    /// <summary>
    /// Indica si la imagen viene del add-in de imágenes (BD compartida) y no de una captura propia.
    /// </summary>
    [JsonIgnore]
    public bool IsFromImagesAddin => !string.IsNullOrEmpty(AddinImageCode);
}
