using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Entities;
using CQEING_CuadroCantidades.Core.Domain.Interfaces;

namespace CQEING_CuadroCantidades.Services;

public sealed class PreviewRefreshService : IPreviewRefreshService
{
    private readonly IPreviewSessionContext _sessionContext;
    private readonly IManualPreviewService _manualPreviewService;
    private readonly IActivityPreviewUpdater _activityPreviewUpdater;
    private readonly IServiceScopeFactory _scopeFactory;

    public PreviewRefreshService(
        IPreviewSessionContext sessionContext,
        IManualPreviewService manualPreviewService,
        IActivityPreviewUpdater activityPreviewUpdater,
        IServiceScopeFactory scopeFactory)
    {
        _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
        _manualPreviewService = manualPreviewService ?? throw new ArgumentNullException(nameof(manualPreviewService));
        _activityPreviewUpdater = activityPreviewUpdater ?? throw new ArgumentNullException(nameof(activityPreviewUpdater));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task<ActivityImageSnapshot?> RefreshPreviewAsync(Guid budgetId, string activityCode, IEnumerable<string> uniqueIds, string? selectedViewUniqueId = null)
    {
        Guid budgetItemId;

        using (var scope = _scopeFactory.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var items = await unitOfWork.BudgetItems.FindAsync(i => i.BudgetId == budgetId && i.Code == activityCode);
            var budgetItem = items.FirstOrDefault();
            if (budgetItem == null)
            {
                throw new InvalidOperationException($"La actividad {activityCode} no fue encontrada en la base de datos.");
            }
            budgetItemId = budgetItem.Id;
        }

        // Iniciar sesión
        _sessionContext.StartSession(budgetItemId, activityCode, uniqueIds);

        try
        {
            // Generar captura
            var snapshot = await _manualPreviewService.GenerateManualPreviewAsync(selectedViewUniqueId);
            if (snapshot == null)
            {
                return null;
            }

            // Guardar en base de datos
            await _activityPreviewUpdater.UpdatePreviewAsync(budgetId, activityCode, snapshot);

            return snapshot;
        }
        finally
        {
            // Finalizar sesión de preview
            _sessionContext.EndSession();
        }
    }
}
