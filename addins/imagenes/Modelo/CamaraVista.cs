namespace practicacq.Imagenes.Modelo;

/// <summary>
/// Cómo estaba la vista de Revit cuando se generó una imagen, para poder rehacerla igual aunque
/// después alguien gire la vista 3D o cambie su recorte (issue #186). Sin tipos de Revit: los puntos
/// van como <c>double[3]</c> (x, y, z en pies, unidades internas de Revit) para guardarse en el JSON.
/// </summary>
public sealed class CamaraVista
{
    /// <summary>3D: posición del ojo (<c>ViewOrientation3D.EyePosition</c>). Vacío en planta.</summary>
    public double[] Ojo { get; set; } = Array.Empty<double>();

    /// <summary>3D: hacia dónde mira (<c>ForwardDirection</c>).</summary>
    public double[] Adelante { get; set; } = Array.Empty<double>();

    /// <summary>3D: hacia dónde queda "arriba" en la imagen (<c>UpDirection</c>).</summary>
    public double[] Arriba { get; set; } = Array.Empty<double>();

    /// <summary>3D: caja de sección activa, o null si la vista no tenía caja de sección.</summary>
    public CajaVista? CajaSeccion { get; set; }

    /// <summary>Planta: región de recorte activa, o null si la vista no recortaba.</summary>
    public CajaVista? Recorte { get; set; }

    public bool TieneOrientacion3D => Ojo.Length == 3 && Adelante.Length == 3 && Arriba.Length == 3;
}

/// <summary>Un <c>BoundingBoxXYZ</c> de Revit: mínimo, máximo y su sistema de coordenadas.</summary>
public sealed class CajaVista
{
    public double[] Min { get; set; } = Array.Empty<double>();
    public double[] Max { get; set; } = Array.Empty<double>();
    public double[] Origen { get; set; } = Array.Empty<double>();
    public double[] EjeX { get; set; } = Array.Empty<double>();
    public double[] EjeY { get; set; } = Array.Empty<double>();
    public double[] EjeZ { get; set; } = Array.Empty<double>();
}
