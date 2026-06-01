// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Admin;



/// <summary>
/// Configures the database mapping for the <see cref="PersonFinancialProfile"/> entity.
/// Defines constraints, foreign keys, and soft-delete filters for financial records.
/// </summary>
public class PersonFinancialProfileConfiguration : IEntityTypeConfiguration<PersonFinancialProfile>
{
    /// <summary>
    /// Configures the <see cref="PersonFinancialProfile"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<PersonFinancialProfile> builder)
    {
        // --- Table & Schema ---
        // Map to table "PersonFinancialProfiles" in schema "Admin"
        builder.ToTable("PersonFinancialProfiles", "Admin");

        // --- Primary Key ---
        // Inherited from BaseAuditableEntity
        builder.HasKey(pfp => pfp.Id);

        // --- Properties ---

        // Source of funds is a descriptive text, needs a reasonable limit to optimize DB space
        builder.Property(pfp => pfp.SourceOfFunds)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(pfp => pfp.DeclaredDate)
            .IsRequired()
            .HasColumnType("datetime2");

        // State flags
        builder.Property(pfp => pfp.IsCurrent)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(pfp => pfp.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // Soft delete flag (assuming the rule GcRecord == 0 means active)
        builder.Property(pfp => pfp.GcRecord)
            .IsRequired()
            .HasDefaultValue(0);

        // --- Indexes ---

        // Index to significantly speed up queries when looking for a specific person's financial history.
        // We include IsCurrent to instantly find the active financial profile.
        builder.HasIndex(pfp => new { pfp.CompanyId, pfp.PersonId, pfp.IsCurrent })
            .HasDatabaseName("IX_PersonFinancialProfiles_Company_Person_Current");

        // --- Relationships ---

        // 1. Relationship with Company (Tenant)
        builder.HasOne(pfp => pfp.Company)
            .WithMany()
            .HasForeignKey(pfp => pfp.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // 2. Relationship with the Core Person
        builder.HasOne(pfp => pfp.Person)
            .WithMany(p => p.FinancialProfiles) // Assuming you have public virtual ICollection<PersonFinancialProfile> FinancialProfiles in Person
            .HasForeignKey(pfp => pfp.PersonId)
            // Restrict prevents accidentally deleting a Person directly via DB cascade, 
            // enforcing the use of the Application Layer to handle logical deletion.
            .OnDelete(DeleteBehavior.Restrict); 

        // 3. Relationship with the IncomeRange Catalog
        builder.HasOne(pfp => pfp.IncomeRange)
            .WithMany()
            .HasForeignKey(pfp => pfp.IncomeRangeId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Query Filters ---

        // Apply the universal soft-delete filter: 0 means active.
        builder.HasQueryFilter(pfp => pfp.GcRecord == 0);
    }
}
