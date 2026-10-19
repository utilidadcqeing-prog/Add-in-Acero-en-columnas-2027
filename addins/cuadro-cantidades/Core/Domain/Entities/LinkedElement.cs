using System;

namespace CQEING_CuadroCantidades.Core.Domain.Entities;

/// <summary>
/// Representa la vinculación de un elemento físico o lógico de Revit con una partida presupuestaria específica.
/// Permite rastrear la cuantificación y estado del elemento en el modelo.
/// </summary>
public sealed class LinkedElement
{
    /// <summary>
    /// Identificador único (GUID) del elemento vinculado en la base de datos local.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Identificador único (GUID) de la partida presupuestaria (BudgetItem) a la que está vinculado este elemento.
    /// </summary>
    public Guid BudgetItemId { get; set; }

    /// <summary>
    /// Identificador único nativo de Revit (UniqueId) del elemento del modelo.
    /// </summary>
    public string RevitUniqueId { get; set; } = string.Empty;

    /// <summary>
    /// Nombre de la categoría de Revit a la que pertenece el elemento (ej: "Walls", "Doors").
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Nombre descriptivo o de tipo del elemento en Revit.
    /// </summary>
    public string ElementName { get; set; } = string.Empty;

    /// <summary>
    /// Cadena JSON para almacenar en caché los valores de los parámetros del elemento,
    /// evitando llamadas directas y lentas a la API de Revit en operaciones de lectura frecuentes.
    /// </summary>
    public string ElementDataJson { get; set; } = "{}";

    /// <summary>
    /// Indica si el elemento asociado ha sido eliminado del modelo de Revit en sesiones recientes.
    /// </summary>
    public bool IsDeletedInRevit { get; set; }

    /// <summary>
    /// URL pública de la imagen de vista previa del elemento en Cloudinary.
    /// </summary>
    public string? UrlImagen { get; set; }

    /// <summary>
    /// Propiedad de navegación a la partida presupuestaria vinculada.
    /// </summary>
    public BudgetItem? BudgetItem { get; set; }
}
