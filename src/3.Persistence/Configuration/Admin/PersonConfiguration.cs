// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Admin;



/// <summary>
/// Configures the database mapping for the <see cref="Person"/> entity.
/// Defines table structure, constraints, multi-tenant isolation, and relationships for the core CRM entity.
/// </summary>
public class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    /// <summary>
    /// Configures the <see cref="Person"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        // --- Table & Schema ---
        builder.ToTable("Persons", "Admin");

        // --- Primary Key ---
        builder.HasKey(c => c.Id);

        // --- Properties ---
        
        // PersonType optimized to integer for database indexing performance
        builder.Property(c => c.PersonType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(c => c.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(c => c.MiddleName).HasMaxLength(100).IsRequired(false);
        builder.Property(c => c.LastName).HasMaxLength(100).IsRequired(false); // Can be null for Legal entities
        builder.Property(c => c.SecondLastName).HasMaxLength(100).IsRequired(false);
        builder.Property(c => c.CommercialName).HasMaxLength(200).IsRequired(false);
        builder.Property(c => c.IdentificationNumber).IsRequired().HasMaxLength(50);

        // State & Soft Delete Flags
        builder.Property(c => c.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(c => c.GcRecord)
            .IsRequired()
            .HasDefaultValue(0);

        // --- Indexes & Unique Constraints ---

        // CRITICAL: Unique index ensuring a customer's identification is unique within a company.
        // GcRecord is included so logically deleted persons don't block new registrations with the same ID.
        builder.HasIndex(c => new { c.CompanyId, c.IdentificationTypeId, c.IdentificationNumber, c.GcRecord })
            .IsUnique()
            .HasDatabaseName("IX_Persons_Company_IdType_IdNumber_GcRecord");

        // --- Relationships (Many-to-One) ---

        // Required relationship with Company (Tenant)
        builder.HasOne(c => c.Company)
            .WithMany()
            .HasForeignKey(c => c.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Required relationship with IdentificationType
        builder.HasOne(c => c.IdentificationType)
            .WithMany()
            .HasForeignKey(c => c.IdentificationTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional relationship with Gender (Nullable FK)
        builder.HasOne(c => c.Gender)
            .WithMany()
            .HasForeignKey(c => c.GenderId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // --- Relationships (One-to-Many Collections) ---
        // CRITICAL: Enforce DeleteBehavior.Restrict to prevent physical cascade deletions

        builder.HasMany(c => c.Addresses)
            .WithOne(ca => ca.Person)
            .HasForeignKey(ca => ca.PersonId)
            .OnDelete(DeleteBehavior.Restrict); 

        builder.HasMany(c => c.Contacts)
            .WithOne(cc => cc.Person)
            .HasForeignKey(cc => cc.PersonId)
            .OnDelete(DeleteBehavior.Restrict);
            
        builder.HasMany(c => c.Tickets)
            .WithOne(t => t.Person)
            .HasForeignKey(t => t.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        // Onboarding Profiles
        builder.HasMany(c => c.EmploymentHistory)
            .WithOne(pe => pe.Person)
            .HasForeignKey(pe => pe.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.BusinessProfiles)
            .WithOne(bp => bp.Person)
            .HasForeignKey(bp => bp.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.FinancialProfiles)
            .WithOne(fp => fp.Person)
            .HasForeignKey(fp => fp.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Query Filters ---

        // Apply the universal soft-delete filter: 0 means active.
        builder.HasQueryFilter(c => c.GcRecord == 0);
    }
}