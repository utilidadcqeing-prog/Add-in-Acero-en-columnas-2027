using System;

namespace CQEING_CuadroCantidades.Core.Domain.Entities;

/// <summary>
/// Representa el estado persistente de la aplicación asociado a un documento específico de Revit.
/// Permite restaurar la sesión del usuario (selecciones, rutas de plantillas, IDs activos) al reabrir el archivo.
/// </summary>
public sealed class AppState
{
    /// <summary>
    /// Ruta del archivo del documento de Revit al cual está asociado este estado.
    /// Funciona como clave única para asociar el estado a un modelo.
    /// </summary>
    public string RevitDocumentPath { get; set; } = string.Empty;

    /// <summary>
    /// Identificador único (GUID) del proyecto actualmente activo.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Identificador único (GUID) del presupuesto actualmente activo.
    /// </summary>
    public Guid BudgetId { get; set; }

    /// <summary>
    /// Código de la fila o partida que estaba seleccionada por última vez en la interfaz.
    /// </summary>
    public string SelectedRowCode { get; set; } = string.Empty;

    /// <summary>
    /// Última ruta de archivo de plantilla de presupuesto cargada en la aplicación.
    /// </summary>
    public string LastStructurePath { get; set; } = string.Empty;

    /// <summary>
    /// Nombre descriptivo de la estructura o plantilla cargada.
    /// </summary>
    public string LoadedStructureName { get; set; } = string.Empty;

    /// <summary>
    /// Nueva descripción de partida temporal ingresada por el usuario en la interfaz.
    /// </summary>
    public string NewDescription { get; set; } = string.Empty;

    /// <summary>
    /// Unidad de medida asignada por defecto a las nuevas partidas creadas (ej: "M3").
    /// </summary>
    public string NewUnit { get; set; } = "M3";

    /// <summary>
    /// Fecha y hora UTC del último guardado automático o manual del estado.
    /// </summary>
    public DateTime LastSaved { get; set; } = DateTime.UtcNow;
}
