// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Infrastructure.Configuration.Admin;

/// <summary>
/// Configures the database mapping for the <see cref="Area"/> entity.
/// Defines table structure, constraints, and relationships for functional departments.
/// </summary>
public class AreaConfiguration : IEntityTypeConfiguration<Area>
{
    /// <summary>
    /// Configures the <see cref="Area"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<Area> builder)
    {
        // Map to table "Areas" in schema "Admin"
        builder.ToTable("Areas", "Admin");

        // Set the primary key
        builder.HasKey(a => a.Id);

        // Configure 'Name' property: required, max length 100
        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(100);

        // --- Relationships ---

        // Required relationship with Company (Tenant)
        // An Area must belong to a Company.
        builder.HasOne(a => a.Company)
            .WithMany()
            .HasForeignKey(a => a.CompanyId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent deleting a Company if it has Areas.

        // Required relationship with EntityStatus
        // An Area must have a status.
        builder.HasOne(a => a.Status)
            .WithMany(es => es.Areas)
            .HasForeignKey(a => a.EntityStatusId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent deleting a Status if it's in use.

        // --- Query Filters ---

        // Apply a soft-delete filter to automatically exclude records marked as deleted.
        builder.HasQueryFilter(a => a.GcRecord == 0);
    }
}
