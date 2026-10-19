using System;

namespace CQEING_CuadroCantidades.Core.Domain.Entities;

/// <summary>
/// Representa una plantilla de estructura o ítem de catálogo predefinido
/// que sirve como base para construir presupuestos nuevos.
/// </summary>
public sealed class TemplateItem
{
    /// <summary>
    /// Identificador único (GUID) del ítem de plantilla.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Nombre descriptivo del ítem o partida predefinida en la plantilla.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Tipo de ítem de plantilla (ej: "Actividad", "Capitulo").
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Datos estructurados de la plantilla almacenados en formato JSON (por ejemplo, parámetros por defecto).
    /// </summary>
    public string DataJson { get; set; } = "{}";
}
