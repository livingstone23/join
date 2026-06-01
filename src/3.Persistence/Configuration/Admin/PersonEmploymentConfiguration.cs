// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Admin;



/// <summary>
/// Configures the database mapping for the <see cref="PersonEmployment"/> entity.
/// Defines constraints, date formatting, and soft-delete filters for employment records.
/// </summary>
public class PersonEmploymentConfiguration : IEntityTypeConfiguration<PersonEmployment>
{
    /// <summary>
    /// Configures the <see cref="PersonEmployment"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<PersonEmployment> builder)
    {
        // --- Table & Schema ---
        // Map to table "PersonEmployments" in schema "Admin"
        builder.ToTable("PersonEmployments", "Admin");

        // --- Primary Key ---
        // Inherited from BaseAuditableEntity
        builder.HasKey(pe => pe.Id);

        // --- Properties ---

        // Employer names can be reasonably long
        builder.Property(pe => pe.EmployerName)
            .IsRequired()
            .HasMaxLength(200);

        // Job titles
        builder.Property(pe => pe.JobTitle)
            .IsRequired()
            .HasMaxLength(150);

        // For employment dates, the specific time (hours/minutes) is usually irrelevant.
        // Using "date" saves database space and simplifies range queries.
        builder.Property(pe => pe.StartDate)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(pe => pe.EndDate)
            .IsRequired(false)
            .HasColumnType("date");

        // State flags
        builder.Property(pe => pe.IsCurrent)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(pe => pe.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // Soft delete flag mapping (inherited from BaseAuditableEntity)
        builder.Property(pe => pe.GcRecord)
            .IsRequired()
            .HasDefaultValue(PersonEmployment.ActiveGcRecord); // Uses the constant defined in BaseAuditableEntity

        // --- Indexes ---

        // Performance Index: Extremely useful for Dapper queries when displaying 
        // the user's profile and we only need to fetch their *current* job quickly.
        builder.HasIndex(pe => new { pe.CompanyId, pe.PersonId, pe.IsCurrent })
            .HasDatabaseName("IX_PersonEmployments_Company_Person_Current");

        // --- Relationships ---

        // 1. Relationship with Company (Tenant)
        // Inherited from BaseTenantEntity
        builder.HasOne(pe => pe.Company)
            .WithMany()
            .HasForeignKey(pe => pe.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // 2. Relationship with the Core Person
        builder.HasOne(pe => pe.Person)
            .WithMany(p => p.EmploymentHistory)
            .HasForeignKey(pe => pe.PersonId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent DB-level cascading; enforce Application-level soft delete

        // --- Query Filters ---

        // Apply the universal soft-delete filter: 0 means active.
        builder.HasQueryFilter(cm => cm.GcRecord == 0);
        
    }
}
