using CQEING_CuadroCantidades.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CQEING_CuadroCantidades.Infrastructure.Data.Context;

/// <summary>
/// Contexto de la base de datos de Entity Framework Core para la aplicación Cuadro de Cantidades.
/// Administra la base de datos en memoria y mapea las entidades del dominio a las tablas correspondientes.
/// </summary>
public sealed class CuadroCantidadesDbContext : DbContext
{
    /// <summary>
    /// Conjunto de datos para la entidad de Proyectos (<see cref="Project"/>).
    /// </summary>
    public DbSet<Project> Projects => Set<Project>();

    /// <summary>
    /// Conjunto de datos para la entidad de Presupuestos (<see cref="Budget"/>).
    /// </summary>
    public DbSet<Budget> Budgets => Set<Budget>();

    /// <summary>
    /// Conjunto de datos para la entidad de Partidas de Presupuesto (<see cref="BudgetItem"/>).
    /// </summary>
    public DbSet<BudgetItem> BudgetItems => Set<BudgetItem>();

    /// <summary>
    /// Conjunto de datos para la entidad de Elementos Vinculados (<see cref="LinkedElement"/>).
    /// </summary>
    public DbSet<LinkedElement> LinkedElements => Set<LinkedElement>();

    /// <summary>
    /// Conjunto de datos para la entidad de Plantillas de Partidas (<see cref="TemplateItem"/>).
    /// </summary>
    public DbSet<TemplateItem> Templates => Set<TemplateItem>();

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="CuadroCantidadesDbContext"/> con las opciones de configuración dadas.
    /// </summary>
    /// <param name="options">Las opciones de configuración para el contexto de base de datos.</param>
    public CuadroCantidadesDbContext(DbContextOptions<CuadroCantidadesDbContext> options)
        : base(options) 
    {
        Database.EnsureCreated();
    }

    /// <summary>
    /// Configura el modelo y las relaciones de base de datos mediante el ModelBuilder.
    /// </summary>
    /// <param name="modelBuilder">El constructor utilizado para construir el modelo de este contexto.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configuración de la entidad Project (Proyecto)
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RevitDocumentPath).IsRequired();
        });

        // Configuración de la entidad Budget (Presupuesto)
        modelBuilder.Entity<Budget>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Relación Uno a Muchos: Un Proyecto puede tener varios Presupuestos.
            entity
                .HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configuración de la entidad BudgetItem (Partida de Presupuesto)
        modelBuilder.Entity<BudgetItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            // Relación Uno a Muchos: Un Presupuesto contiene muchas Partidas.
            entity
                .HasOne(e => e.Budget)
                .WithMany(b => b.Items)
                .HasForeignKey(e => e.BudgetId)
                .OnDelete(DeleteBehavior.Cascade);

            // Relación Jerárquica (Autorelación): Una partida puede tener una partida padre y múltiples partidas hijas.
            entity
                .HasOne(e => e.Parent)
                .WithMany(p => p.Children)
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configuración de la entidad LinkedElement (Elemento Vinculado)
        modelBuilder.Entity<LinkedElement>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            // Relación Uno a Muchos: Una Partida de Presupuesto contiene múltiples Elementos Vinculados de Revit.
            entity
                .HasOne(e => e.BudgetItem)
                .WithMany(b => b.LinkedElements)
                .HasForeignKey(e => e.BudgetItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configuración de la entidad TemplateItem (Plantilla de Partidas)
        modelBuilder.Entity<TemplateItem>(entity =>
        {
            entity.HasKey(e => e.Id);
        });
    }
}
