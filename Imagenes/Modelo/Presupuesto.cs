namespace practicacq.Imagenes.Modelo;

/// <summary>
/// Un presupuesto tal como lo lista <c>GET /api/presupuestos</c>.
///
/// No lleva vínculo con el modelo de Revit (decisión del 2026-10-13): el modelo se vincula a cada
/// IMAGEN de una actividad (<see cref="ImagenActividad.IdModelo"/>), no al presupuesto. Por eso desde
/// cualquier modelo se pueden crear imágenes en cualquier presupuesto, y el <c>revit_model_id</c> del
/// presupuesto (que usa el add-in de cantidades) aquí no se mira.
/// </summary>
public sealed class PresupuestoResumen
{
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;

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
    /// <summary><c>id_item</c> de la API: las imágenes se registran en la BD contra este id (fase B).</summary>
    public int IdItem { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string Unidad { get; init; } = string.Empty;
}
