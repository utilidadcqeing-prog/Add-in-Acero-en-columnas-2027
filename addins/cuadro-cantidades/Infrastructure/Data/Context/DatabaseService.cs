using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CQEING_CuadroCantidades.Infrastructure.Data.Context;

/// <summary>
/// Servicio responsable de la inicialización y el mantenimiento del esquema de la base de datos en memoria.
/// </summary>
public class DatabaseService : IDatabaseService
{
    /// <summary>
    /// Contexto de base de datos utilizado para las operaciones de inicialización.
    /// </summary>
    private readonly CuadroCantidadesDbContext _context;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="DatabaseService"/> con el contexto de base de datos especificado.
    /// </summary>
    /// <param name="context">El contexto de base de datos del Cuadro de Cantidades.</param>
    public DatabaseService(CuadroCantidadesDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Inicializa la base de datos de forma asíncrona, asegurando que el esquema esté creado en memoria.
    /// </summary>
    /// <returns>Una tarea que representa la operación de inicialización asíncrona.</returns>
    public async Task InitializeDatabaseAsync()
    {
        // Asegura que la base de datos y su esquema existan en memoria.
        await _context.Database.EnsureCreatedAsync();
    }
}
