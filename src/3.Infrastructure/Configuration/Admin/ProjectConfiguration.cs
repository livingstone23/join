// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Infrastructure.Configuration.Admin;



/// <summary>
/// Configures the database mapping for the <see cref="Project"/> entity.
/// Defines table structure, constraints, and relationships for business projects.
/// </summary>
public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{

    /// <summary>
    /// Configures the <see cref="Project"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<Project> builder)
    {

        // Map to table "Projects" in schema "Admin"
        builder.ToTable("Projects", "Admin");

        // Set the primary key
        builder.HasKey(p => p.Id);

        // --- Properties ---

        builder.Property(p => p.Name).IsRequired().HasMaxLength(150);

        // --- Relationships ---

        // Required relationship with Company (Tenant)
        builder.HasOne(p => p.Company)
            .WithMany()
            .HasForeignKey(p => p.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Required relationship with EntityStatus
        builder.HasOne(p => p.Status)
            .WithMany(es => es.Projects)
            .HasForeignKey(p => p.EntityStatusId)
            .OnDelete(DeleteBehavior.Restrict);
            
        // One-to-many relationship with Ticket
        builder.HasMany(p => p.Tickets)
            .WithOne(t => t.Project)
            .HasForeignKey(t => t.ProjectId);

        // --- Query Filters ---

        // Apply a soft-delete filter.
        builder.HasQueryFilter(p => p.GcRecord == 0);

    }

}
