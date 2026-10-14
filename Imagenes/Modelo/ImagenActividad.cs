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

    // ── Para actualizar la imagen cuando cambia el modelo (issue #186). Vacíos en imágenes anteriores. ──

    /// <summary><c>UniqueId</c> de la vista desde la que se generó.</summary>
    public string UniqueIdVista { get; set; } = string.Empty;

    /// <summary>Si se seleccionó con "mismo tipo": al actualizar entran también los elementos nuevos de esos tipos.</summary>
    public bool MismoTipo { get; set; }

    /// <summary>Cámara (3D) o recorte (planta) con que se generó.</summary>
    public CamaraVista? Camara { get; set; }

    /// <summary>
    /// Cuántas veces se generó: 1 al crearla, +1 cada vez que se actualiza. El código no cambia; la
    /// versión solo va en el <c>public_id</c> de Cloudinary (<see cref="PublicIdDeVersion"/>).
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary><c>public_id</c> que devolvió Cloudinary (puede incluir la carpeta). Vacío si no se subió.</summary>
    public string PublicIdCloudinary { get; set; } = string.Empty;

    /// <summary>Versiones anteriores que quedan por borrar en Cloudinary (faltan las claves o falló el borrado).</summary>
    public List<string> PublicIdsPorBorrar { get; set; } = new();

    /// <summary>Ruta del PNG guardado en esta PC.</summary>
    public string RutaLocal { get; set; } = string.Empty;

    /// <summary>URL de Cloudinary. Vacía mientras la subida esté pendiente o haya fallado.</summary>
    public string? Url { get; set; }

    public DateTime Fecha { get; set; }

    [JsonIgnore]
    public bool SubidaPendiente => string.IsNullOrEmpty(Url);

    [JsonIgnore]
    public bool BorradoPendiente => PublicIdsPorBorrar.Count > 0;

    /// <summary>
    /// Si guarda lo necesario para rehacerla sola. Las imágenes anteriores a la issue #186 no tienen
    /// los <c>UniqueId</c> y hay que rehacerlas con "Nueva imagen".
    /// </summary>
    [JsonIgnore]
    public bool TieneDatosParaActualizar =>
        Colores.Count > 0 && Colores.All(c => c.UniqueIdsElementos.Count > 0) && Camara is not null;

    /// <summary>
    /// Nombre en Cloudinary de una versión: la 1 usa el código tal cual (<c>ACT_006_01</c>, como las
    /// imágenes de antes) y las siguientes llevan sufijo (<c>ACT_006_01_v2</c>). Las subidas sin firma
    /// no sobrescriben, por eso cada versión necesita un nombre propio.
    /// </summary>
    public string PublicIdDeVersion(int version) => version <= 1 ? Codigo : $"{Codigo}_v{version}";

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
    public string TextoEstadoSubida =>
        SubidaPendiente ? "Solo en esta PC (subida pendiente)"
        : BorradoPendiente ? "Subida a Cloudinary (falta borrar la versión anterior)"
        : "Subida a Cloudinary";
}

/// <summary>El color que se le puso a un tipo de Revit en una imagen. Sirve de leyenda.</summary>
public sealed class ColorDeTipo
{
    public string NombreTipo { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;

    /// <summary>Id del tipo de Revit, para volver a buscar los elementos de ese tipo con "mismo tipo".</summary>
    public long IdTipo { get; set; }

    /// <summary>Elementos pintados con este color, por <c>UniqueId</c> (issue #186).</summary>
    public List<string> UniqueIdsElementos { get; set; } = new();
}
