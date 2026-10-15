namespace practicacq.Imagenes.Modelo;

/// <summary>
/// Un tipo de Revit dentro de lo que el usuario seleccionó (por ejemplo, "Hormigón-Rectangular-Pilar :
/// (0.30 x 0.30)"). Cada tipo se pinta con un solo color en la imagen.
/// </summary>
public sealed class TipoSeleccionado
{
    public long IdTipo { get; init; }
    public string Categoria { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public List<long> IdsElementos { get; init; } = new();

    /// <summary>
    /// Los mismos elementos por <c>UniqueId</c>, que es lo que se guarda en la imagen para poder
    /// rehacerla después (issue #186): el <c>ElementId</c> no es una referencia estable fuera de Revit.
    /// </summary>
    public List<string> UniqueIdsElementos { get; init; } = new();
    public string ColorHex { get; set; } = string.Empty;

    public string TextoCantidad => IdsElementos.Count == 1 ? "1 elemento" : $"{IdsElementos.Count} elementos";
}

/// <summary>Lo que devuelve Revit al pulsar Aceptar en la barra de selección.</summary>
public sealed class ResultadoSeleccion
{
    public List<TipoSeleccionado> Tipos { get; init; } = new();

    /// <summary>
    /// Vista donde se seleccionó. La imagen se genera desde ella aunque después el usuario cambie de
    /// vista en Revit.
    /// </summary>
    public long IdVista { get; init; }
    public string NombreVista { get; init; } = string.Empty;
    public string UniqueIdVista { get; init; } = string.Empty;

    /// <summary>Si se seleccionó con "mismo tipo": al actualizar se vuelven a sumar los elementos nuevos de esos tipos.</summary>
    public bool MismoTipo { get; init; }

    /// <summary>
    /// Cámara de la vista en el momento de exportar el PNG. La llena <c>PuenteRevit.Capturar</c>, así
    /// que siempre corresponde a la última imagen generada (vista previa o la definitiva).
    /// </summary>
    public CamaraVista? Camara { get; set; }

    /// <summary>"3D" o "Planta".</summary>
    public string TipoVista { get; init; } = string.Empty;

    public int TotalElementos => Tipos.Sum(t => t.IdsElementos.Count);
}
