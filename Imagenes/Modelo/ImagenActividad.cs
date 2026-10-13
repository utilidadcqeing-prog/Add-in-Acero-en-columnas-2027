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

    /// <summary>
    /// Modelo de Revit del que salió la imagen: el vínculo modelo ↔ actividad vive aquí, en cada
    /// imagen, y no en el presupuesto. Mismo id que calcula el add-in de cantidades
    /// (<c>RevitModelIdentifier</c>), para poder cruzarlos después. Vacío en imágenes guardadas antes
    /// de este cambio.
    /// </summary>
    public string IdModelo { get; set; } = string.Empty;

    /// <summary>Nombre del archivo del modelo (título del documento), para mostrarlo en el tablero.</summary>
    public string NombreModelo { get; set; } = string.Empty;

    public List<ColorDeTipo> Colores { get; set; } = new();

    /// <summary>Ruta del PNG guardado en esta PC.</summary>
    public string RutaLocal { get; set; } = string.Empty;

    /// <summary>URL de Cloudinary. Vacía mientras la subida esté pendiente o haya fallado.</summary>
    public string? Url { get; set; }

    public DateTime Fecha { get; set; }

    [JsonIgnore]
    public bool SubidaPendiente => string.IsNullOrEmpty(Url);

    /// <summary>
    /// Si la imagen salió del modelo con id <paramref name="idModelo"/>. Se compara solo la parte
    /// antes de <c>:</c>: en modelos colaborativos lo que sigue es la copia local de cada usuario
    /// (ver <c>RevitModelIdentifier</c> del add-in de cantidades), y una imagen hecha por otra persona
    /// desde el mismo modelo central es del mismo modelo.
    /// </summary>
    public bool EsDelModelo(string idModelo) =>
        IdModelo.Length > 0 && ParteCentral(IdModelo) == ParteCentral(idModelo);

    private static string ParteCentral(string id) => id.Split(':')[0];

    [JsonIgnore]
    public string TextoEstadoSubida => SubidaPendiente ? "Solo en esta PC (subida pendiente)" : "Subida a Cloudinary";
}

/// <summary>El color que se le puso a un tipo de Revit en una imagen. Sirve de leyenda.</summary>
public sealed class ColorDeTipo
{
    public string NombreTipo { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
}
