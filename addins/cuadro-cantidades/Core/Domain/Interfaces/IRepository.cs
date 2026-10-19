using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace CQEING_CuadroCantidades.Core.Domain.Interfaces;

/// <summary>
/// Interfaz genérica para el patrón Repositorio, que define las operaciones CRUD básicas y de consulta
/// para entidades del dominio.
/// </summary>
/// <typeparam name="TEntity">El tipo de la entidad del dominio.</typeparam>
public interface IRepository<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Obtiene una entidad por su identificador único de forma asíncrona.
    /// </summary>
    /// <param name="id">El identificador único de la entidad.</param>
    /// <returns>La entidad encontrada o <see langword="null"/> si no existe.</returns>
    Task<TEntity?> GetByIdAsync(Guid id);

    /// <summary>
    /// Obtiene todas las entidades de forma asíncrona.
    /// </summary>
    /// <returns>Una colección de todas las entidades existentes.</returns>
    Task<IEnumerable<TEntity>> GetAllAsync();

    /// <summary>
    /// Busca entidades que cumplan con una condición o predicado específico de forma asíncrona.
    /// </summary>
    /// <param name="predicate">La expresión lambda que define la condición de búsqueda.</param>
    /// <returns>Una colección de entidades que cumplen con la condición especificada.</returns>
    Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate);

    /// <summary>
    /// Agrega una nueva entidad al repositorio de forma asíncrona.
    /// </summary>
    /// <param name="entity">La entidad a agregar.</param>
    Task AddAsync(TEntity entity);

    /// <summary>
    /// Agrega un rango de nuevas entidades al repositorio de forma asíncrona.
    /// </summary>
    /// <param name="entities">La colección de entidades a agregar.</param>
    Task AddRangeAsync(IEnumerable<TEntity> entities);

    /// <summary>
    /// Marca una entidad existente para ser actualizada en el repositorio.
    /// </summary>
    /// <param name="entity">La entidad a actualizar.</param>
    void Update(TEntity entity);

    /// <summary>
    /// Marca una entidad existente para ser eliminada del repositorio.
    /// </summary>
    /// <param name="entity">La entidad a eliminar.</param>
    void Remove(TEntity entity);

    /// <summary>
    /// Marca un rango de entidades para ser eliminadas del repositorio.
    /// </summary>
    /// <param name="entities">La colección de entidades a eliminar.</param>
    void RemoveRange(IEnumerable<TEntity> entities);
}

