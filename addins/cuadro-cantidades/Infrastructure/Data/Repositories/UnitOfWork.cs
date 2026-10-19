using System.Threading;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Domain.Entities;
using CQEING_CuadroCantidades.Core.Domain.Interfaces;
using CQEING_CuadroCantidades.Infrastructure.Data.Context;

namespace CQEING_CuadroCantidades.Infrastructure.Data.Repositories;

/// <summary>
/// Implementación de la Unidad de Trabajo (Unit of Work) utilizando Entity Framework Core en memoria.
/// Coordina las transacciones y la persistencia de los diferentes repositorios del dominio.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Contexto de la base de datos compartido entre todos los repositorios.
    /// </summary>
    private readonly CuadroCantidadesDbContext _context;

    /// <summary>
    /// Repositorio para la gestión de proyectos.
    /// </summary>
    public IRepository<Project> Projects { get; private set; }

    /// <summary>
    /// Repositorio para la gestión de presupuestos.
    /// </summary>
    public IRepository<Budget> Budgets { get; private set; }

    /// <summary>
    /// Repositorio para la gestión de partidas del presupuesto.
    /// </summary>
    public IRepository<BudgetItem> BudgetItems { get; private set; }

    /// <summary>
    /// Repositorio para la gestión de elementos vinculados.
    /// </summary>
    public IRepository<LinkedElement> LinkedElements { get; private set; }

    /// <summary>
    /// Repositorio para la gestión de plantillas de partidas.
    /// </summary>
    public IRepository<TemplateItem> Templates { get; private set; }

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="UnitOfWork"/> inyectando el contexto de la base de datos
    /// e instanciando cada uno de los repositorios específicos.
    /// </summary>
    /// <param name="context">El contexto de base de datos de Entity Framework Core.</param>
    public UnitOfWork(CuadroCantidadesDbContext context)
    {
        _context = context;
        Projects = new Repository<Project>(_context);
        Budgets = new Repository<Budget>(_context);
        BudgetItems = new Repository<BudgetItem>(_context);
        LinkedElements = new Repository<LinkedElement>(_context);
        Templates = new Repository<TemplateItem>(_context);
    }

    /// <summary>
    /// Guarda de forma asíncrona todos los cambios realizados en el contexto de base de datos.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelación opcional para abortar la operación.</param>
    /// <returns>El número de filas afectadas o modificadas en la base de datos.</returns>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Limpia el rastreador de cambios del contexto de base de datos, cancelando cualquier cambio pendiente.
    /// </summary>
    public void ClearTracker()
    {
        _context.ChangeTracker.Clear();
    }

    /// <summary>
    /// Libera los recursos administrados por el contexto de la base de datos.
    /// </summary>
    public void Dispose()
    {
        _context.Dispose();
    }
}

