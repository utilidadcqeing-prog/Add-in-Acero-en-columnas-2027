namespace practicacq.Imagenes.Modelo;

/// <summary>
/// Cómo se relaciona un presupuesto de la API con el modelo de Revit abierto.
/// Decide si en el add-in se pueden crear imágenes o solo mirarlas.
/// </summary>
public enum EstadoVinculo
{
    SinVinculo,
    VinculadoAEsteModelo,
    VinculadoAOtroModelo,
}

/// <summary>Un presupuesto tal como lo lista <c>GET /api/presupuestos</c>.</summary>
public sealed class PresupuestoResumen
{
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public string? RevitModelId { get; init; }
    public EstadoVinculo Estado { get; set; }

    /// <summary>
    /// Vinculado a otro modelo: se ven las imágenes pero no se crean (issue #183, paso 1).
    /// </summary>
    public bool SoloLectura => Estado == EstadoVinculo.VinculadoAOtroModelo;

    public string TextoEstado => Estado switch
    {
        EstadoVinculo.VinculadoAEsteModelo => "Vinculado a este modelo",
        EstadoVinculo.VinculadoAOtroModelo => "Vinculado a otro modelo · solo lectura",
        _ => "Sin vínculo",
    };

    public string TextoLista => $"{Codigo} — {Nombre}";
}

public sealed class Capitulo
{
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public List<Subcapitulo> Subcapitulos { get; init; } = new();
}

public sealed class Subcapitulo
{
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public List<ActividadPresupuesto> Actividades { get; init; } = new();
}

/// <summary>
/// Una actividad de la estructura del presupuesto. Las imágenes se identifican por
/// <see cref="Codigo"/> (<c>codigo_actividad</c>), nunca por el número de ítem, que cambia al
/// reordenar.
/// </summary>
public sealed class ActividadPresupuesto
{
    public string Codigo { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string Unidad { get; init; } = string.Empty;
}
