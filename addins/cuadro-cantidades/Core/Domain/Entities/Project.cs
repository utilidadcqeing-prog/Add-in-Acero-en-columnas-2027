using System;

namespace CQEING_CuadroCantidades.Core.Domain.Entities;

/// <summary>
/// Representa un proyecto dentro de la aplicación, el cual se asocia biunívocamente
/// a un archivo de modelo de Revit mediante su ruta física.
/// </summary>
public sealed class Project
{
    /// <summary>
    /// Identificador único (GUID) del proyecto.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Ruta del archivo local o de red del documento de Revit asociado al proyecto.
    /// </summary>
    public string RevitDocumentPath { get; set; } = string.Empty;

    /// <summary>
    /// Código o número de identificación oficial asignado al proyecto (ej: número de contrato o de obra).
    /// </summary>
    public string ProjectNumber { get; set; } = string.Empty;

    /// <summary>
    /// Fecha y hora UTC en la que se registró el proyecto en la base de datos local.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
