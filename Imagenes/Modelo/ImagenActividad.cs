using System.Text.Json.Serialization;

namespace practicacq.Imagenes.Modelo;

/// <summary>
/// Una imagen ya guardada de una actividad. Es lo que se escribe en el registro local (JSON) y lo
/// que muestra el tablero.
/// </summary>
public sealed class ImagenActividad
{
    /// <summary>Código de la imagen: <c>{codigo_actividad}_{NN}</c>, por ejemplo <c>ACT_006_01</c>.</summary>
    public string Codigo { get; set; } = string.Empty;
    public string CodigoActividad { get; set; } = string.Empty;
    public string CodigoPresupuesto { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;

    /// <summary>"3D" o "Planta".</summary>
    public string TipoVista { get; set; } = string.Empty;

    /// <summary>Nombre de la vista de Revit desde la que se generó, para poder rehacerla.</summary>
    public string NombreVistaRevit { get; set; } = string.Empty;

    public List<ColorDeTipo> Colores { get; set; } = new();

    /// <summary>Ruta del PNG guardado en esta PC.</summary>
    public string RutaLocal { get; set; } = string.Empty;

    /// <summary>URL de Cloudinary. Vacía mientras la subida esté pendiente o haya fallado.</summary>
    public string? Url { get; set; }

    public DateTime Fecha { get; set; }

    [JsonIgnore]
    public bool SubidaPendiente => string.IsNullOrEmpty(Url);

    [JsonIgnore]
    public string TextoEstadoSubida => SubidaPendiente ? "Solo en esta PC (subida pendiente)" : "Subida a Cloudinary";
}

/// <summary>El color que se le puso a un tipo de Revit en una imagen. Sirve de leyenda.</summary>
public sealed class ColorDeTipo
{
    public string NombreTipo { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
}
