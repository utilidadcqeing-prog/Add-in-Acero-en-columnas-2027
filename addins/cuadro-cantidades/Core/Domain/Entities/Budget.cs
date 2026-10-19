using System;
using System.Collections.Generic;

namespace CQEING_CuadroCantidades.Core.Domain.Entities;

/// <summary>
/// Representa un presupuesto asociado a un proyecto de cuantificación.
/// Contiene una versión específica de las partidas presupuestarias (BudgetItem) del modelo.
/// </summary>
public sealed class Budget
{
    /// <summary>
    /// Identificador único (GUID) del presupuesto.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Identificador único (GUID) del proyecto al que pertenece este presupuesto.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Nombre o código de la versión del presupuesto (ej: "V1.0", "Revision A").
    /// </summary>
    public string VersionName { get; set; } = "V1.0";

    /// <summary>
    /// Nombre para visualización en la interfaz (no se guarda en la BD).
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Indica si este presupuesto es el activo actualmente en el proyecto.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Fecha y hora UTC en la que se creó el presupuesto.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Identificador único del modelo de Revit asociado a este presupuesto.
    /// </summary>
    public string? RevitModelId { get; set; }

    /// <summary>
    /// Propiedad de navegación al proyecto asociado a este presupuesto.
    /// </summary>
    public Project? Project { get; set; }

    /// <summary>
    /// Colección de partidas o ítems que conforman este presupuesto.
    /// </summary>
    public ICollection<BudgetItem> Items { get; set; } = new List<BudgetItem>();
}
