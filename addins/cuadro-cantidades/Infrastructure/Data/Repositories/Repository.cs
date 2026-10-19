using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CQEING_CuadroCantidades.Infrastructure.Data.Repositories;

/// <summary>
/// Implementación genérica del patrón Repositorio utilizando Entity Framework Core.
/// Proporciona operaciones comunes de consulta y persistencia para cualquier entidad.
/// </summary>
/// <typeparam name="TEntity">El tipo de la entidad administrada por el repositorio.</typeparam>
public class Repository<TEntity> : IRepository<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Contexto de la base de datos subyacente.
    /// </summary>
    protected readonly DbContext Context;

    /// <summary>
    /// Conjunto de datos de Entity Framework correspondiente a la entidad del repositorio.
    /// </summary>
    protected readonly DbSet<TEntity> DbSet;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="Repository{TEntity}"/> con el contexto especificado.
    /// </summary>
    /// <param name="context">El contexto de la base de datos de Entity Framework.</param>
    public Repository(DbContext context)
    {
        Context = context;
        DbSet = context.Set<TEntity>();
    }

    /// <summary>
    /// Agrega una entidad a la base de datos de forma asíncrona.
    /// </summary>
    /// <param name="entity">La entidad que se va a agregar.</param>
    public async Task AddAsync(TEntity entity)
    {
        await DbSet.AddAsync(entity);
    }

    /// <summary>
    /// Agrega un conjunto de entidades a la base de datos de forma asíncrona.
    /// </summary>
    /// <param name="entities">La colección de entidades que se van a agregar.</param>
    public async Task AddRangeAsync(IEnumerable<TEntity> entities)
    {
        await DbSet.AddRangeAsync(entities);
    }

    /// <summary>
    /// Busca entidades que cumplan con la condición especificada de forma asíncrona.
    /// </summary>
    /// <param name="predicate">La expresión que define la condición de búsqueda.</param>
    /// <returns>Una colección de entidades que coinciden con el predicado.</returns>
    public async Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate)
    {
        return await DbSet.Where(predicate).ToListAsync();
    }

    /// <summary>
    /// Obtiene todas las entidades de la base de datos de forma asíncrona.
    /// </summary>
    /// <returns>Una colección con todas las entidades.</returns>
    public async Task<IEnumerable<TEntity>> GetAllAsync()
    {
        return await DbSet.ToListAsync();
    }

    /// <summary>
    /// Obtiene una entidad por su identificador único de forma asíncrona.
    /// </summary>
    /// <param name="id">El identificador de la entidad.</param>
    /// <returns>La entidad encontrada o <see langword="null"/> si no existe.</returns>
    public async Task<TEntity?> GetByIdAsync(Guid id)
    {
        return await DbSet.FindAsync(id);
    }

    /// <summary>
    /// Elimina una entidad del conjunto de datos.
    /// </summary>
    /// <param name="entity">La entidad que se va a eliminar.</param>
    public void Remove(TEntity entity)
    {
        DbSet.Remove(entity);
    }

    /// <summary>
    /// Elimina una colección de entidades del conjunto de datos.
    /// </summary>
    /// <param name="entities">La colección de entidades que se van a eliminar.</param>
    public void RemoveRange(IEnumerable<TEntity> entities)
    {
        DbSet.RemoveRange(entities);
    }

    /// <summary>
    /// Actualiza una entidad en el conjunto de datos.
    /// </summary>
    /// <param name="entity">La entidad que se va a actualizar.</param>
    public void Update(TEntity entity)
    {
        DbSet.Update(entity);
    }
}

