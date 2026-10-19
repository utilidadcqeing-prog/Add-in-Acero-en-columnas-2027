using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

/// <summary>
/// Guarda en disco los códigos de las actividades con cambios locales que aún no se enviaron a la API,
/// para que un cierre de Revit antes de "Guardar Modelo" no los deje sin enviar.
/// </summary>
public interface IPendingActivitySyncStore
{
    Task<IReadOnlyCollection<string>> LoadAsync(Guid budgetId);

    Task SaveAsync(Guid budgetId, IReadOnlyCollection<string> activityCodes);
}
