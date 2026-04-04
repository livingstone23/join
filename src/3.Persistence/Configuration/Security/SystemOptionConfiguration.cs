// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Security;



/// <summary>
/// Configures the database mapping for the <see cref="SystemOption"/> entity.
/// Defines the structure for individual menu items, screens, or UI actions.
/// </summary>
public class SystemOptionConfiguration : IEntityTypeConfiguration<SystemOption>
{
    /// <summary>
    /// Configures the <see cref="SystemOption"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<SystemOption> builder)
    {
        // Map to table "SystemOptions" in schema "Security"
        builder.ToTable("SystemOptions", "Security");
        builder.HasKey(o => o.Id);

        // --- Properties ---
        builder.Property(o => o.Name).IsRequired().HasMaxLength(150);
        builder.Property(o => o.Route).IsRequired().HasMaxLength(250);
        builder.Property(o => o.Icon).HasMaxLength(100);

        // ControllerName es nulo para las opciones "Padre" (agrupadores)
        builder.Property(o => o.ControllerName)
            .IsRequired(false) 
            .HasMaxLength(250);

        builder.Property(o => o.CanRead).HasDefaultValue(true);
        builder.Property(o => o.CanCreate).HasDefaultValue(true);
        builder.Property(o => o.CanUpdate).HasDefaultValue(true);
        builder.Property(o => o.CanDelete).HasDefaultValue(true);

        // --- Relationships ---
        // Relación con SystemModule
        builder.HasOne(o => o.Module)
            .WithMany(m => m.SystemOptions)
            .HasForeignKey(o => o.ModuleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Relación Jerárquica (Padre-Hijo)
        builder.HasOne(o => o.Parent)
            .WithMany(p => p.Children)
            .HasForeignKey(o => o.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Indexes ---
        builder.HasIndex(o => new { o.ModuleId, o.Route }).IsUnique();
        
        // --- Query Filters ---
        builder.HasQueryFilter(o => o.GcRecord == 0);
    }
}
