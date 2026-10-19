using System;
using System.Collections.Generic;

namespace CQEING_CuadroCantidades.Core.Domain.Entities;

/// <summary>
/// Representa una partida presupuestaria o ítem dentro de la estructura de un presupuesto.
/// Puede ser de tipo CAPITULO, SUBCAPITULO o ACTIVIDAD, y admite una estructura jerárquica (padres e hijos).
/// </summary>
public sealed class BudgetItem
{
    /// <summary>
    /// Identificador único (GUID) del ítem del presupuesto.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Identificador único (GUID) del presupuesto al que pertenece esta partida.
    /// </summary>
    public Guid BudgetId { get; set; }

    /// <summary>
    /// Identificador único (GUID) del ítem padre, si este ítem forma parte de un capítulo o subcapítulo.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Tipo de ítem de presupuesto. Los valores posibles suelen ser: CAPITULO, SUBCAPITULO, ACTIVIDAD.
    /// </summary>
    public string ItemType { get; set; } = string.Empty;

    /// <summary>
    /// Código de identificación o numeración jerárquica de la partida (ej: "1.2.1").
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Descripción detallada del trabajo o alcance de la partida.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Unidad de medida para cuantificar la partida (ej: "M2", "M3", "KG", "UND").
    /// </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// Orden de visualización secuencial de la partida en el presupuesto.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Ruta del archivo local o identificador de la imagen de vista previa de la actividad.
    /// </summary>
    public string? PreviewImagePath { get; set; }

    /// <summary>
    /// Fecha y hora UTC del último cambio o generación de la previsualización 3D.
    /// </summary>
    public DateTime? LastPreviewUpdate { get; set; }

    /// <summary>
    /// Cadena que contiene los IDs de los elementos Revit vinculados en el momento de realizar la captura.
    /// </summary>
    public string? ElementIdsSnapshot { get; set; }

    /// <summary>
    /// Datos serializados en formato JSON que contienen información detallada de múltiples capturas o previsualizaciones.
    /// </summary>
    public string? PreviewImagesJson { get; set; }

    /// <summary>
    /// Propiedad de navegación al presupuesto contenedor al que pertenece la partida.
    /// </summary>
    public Budget? Budget { get; set; }

    /// <summary>
    /// Propiedad de navegación a la partida jerárquica superior (padre).
    /// </summary>
    public BudgetItem? Parent { get; set; }

    /// <summary>
    /// Colección de partidas jerárquicamente inferiores (hijos).
    /// </summary>
    public ICollection<BudgetItem> Children { get; set; } = new List<BudgetItem>();

    /// <summary>
    /// Colección de elementos del modelo de Revit vinculados directamente a esta partida para su cuantificación.
    /// </summary>
    public ICollection<LinkedElement> LinkedElements { get; set; } = new List<LinkedElement>();
}
