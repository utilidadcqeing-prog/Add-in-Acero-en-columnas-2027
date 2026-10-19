using System;
using System.Threading;
using System.Threading.Tasks;

namespace CQEING_CuadroCantidades.Core.Domain.Interfaces;

/// <summary>
/// Interfaz para el patrón Unidad de Trabajo (Unit of Work), que coordina el trabajo
/// de múltiples repositorios y maneja la persistencia de cambios en la base de datos de manera atómica.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// Repositorio para la gestión de entidades de Proyecto (<see cref="Entities.Project"/>).
    /// </summary>
    IRepository<Entities.Project> Projects { get; }

    /// <summary>
    /// Repositorio para la gestión de entidades de Presupuesto (<see cref="Entities.Budget"/>).
    /// </summary>
    IRepository<Entities.Budget> Budgets { get; }

    /// <summary>
    /// Repositorio para la gestión de entidades de Partidas del Presupuesto (<see cref="Entities.BudgetItem"/>).
    /// </summary>
    IRepository<Entities.BudgetItem> BudgetItems { get; }

    /// <summary>
    /// Repositorio para la gestión de entidades de Elementos Vinculados (<see cref="Entities.LinkedElement"/>).
    /// </summary>
    IRepository<Entities.LinkedElement> LinkedElements { get; }

    /// <summary>
    /// Repositorio para la gestión de entidades de Plantillas de Partidas (<see cref="Entities.TemplateItem"/>).
    /// </summary>
    IRepository<Entities.TemplateItem> Templates { get; }

    /// <summary>
    /// Guarda todos los cambios realizados en el contexto de base de datos de forma asíncrona.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelación opcional para anular la operación.</param>
    /// <returns>El número de filas modificadas o afectadas en la base de datos.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Limpia el rastreador de cambios del contexto de base de datos, cancelando cualquier cambio pendiente.
    /// </summary>
    void ClearTracker();
}

