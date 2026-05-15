// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Admin;



/// <summary>
/// Configures the database mapping for the <see cref="PersonContact"/> entity.
/// Defines table structure, constraints, multi-tenant isolation, and performance indexes for customer contacts.
/// </summary>
public class PersonContactConfiguration : IEntityTypeConfiguration<PersonContact>
{
    /// <summary>
    /// Configures the <see cref="PersonContact"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<PersonContact> builder)
    {
        // --- Table & Schema ---
        // Map to table "PersonContacts" in schema "Admin"
        builder.ToTable("PersonContacts", "Admin");

        // --- Primary Key ---
        builder.HasKey(cc => cc.Id);

        // --- Properties ---

        // ContactType is an enum, configured to be stored as an integer for DB efficiency.
        builder.Property(c => c.ContactType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(cc => cc.ContactValue)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(cc => cc.Comments)
            .HasMaxLength(500)
            .IsRequired(false);

        // State flags
        builder.Property(cc => cc.IsPrimary)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(cc => cc.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // Soft delete flag mapping (0 means active)
        builder.Property(cc => cc.GcRecord)
            .IsRequired()
            .HasDefaultValue(0); // Assuming 0 is BaseAuditableEntity.ActiveGcRecord

        // --- Indexes & Unique Constraints ---

        // 1. Performance Index: Crucial for CQRS/Dapper reads.
        // Instantly locates the active, primary contact methods for a specific person.
        builder.HasIndex(cc => new { cc.CompanyId, cc.PersonId, cc.IsPrimary })
            .HasDatabaseName("IX_PersonContacts_Company_Person_Primary");

        // 2. Integrity Constraint: 
        // Prevents the exact same contact value (e.g., the same email) from being registered 
        // multiple times for the same person, ignoring logically deleted ones.
        builder.HasIndex(cc => new { cc.PersonId, cc.ContactType, cc.ContactValue, cc.GcRecord })
            .IsUnique()
            .HasDatabaseName("IX_PersonContacts_Unique_ValuePerPerson");

        // --- Relationships ---

        // 1. Required relationship with Company (Tenant)
        builder.HasOne(cc => cc.Company)
            .WithMany()
            .HasForeignKey(cc => cc.CompanyId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent DB cascade; tenant isolation.

        // 2. Required relationship with Person
        builder.HasOne(cc => cc.Person)
            .WithMany(c => c.Contacts)
            .HasForeignKey(cc => cc.PersonId)
            // Restrict enforces the Domain behavior: The Application layer must iterate 
            // and perform Soft Deletes (GcRecord) rather than letting SQL physically delete records.
            .OnDelete(DeleteBehavior.Restrict); 

        // --- Query Filters ---

        // Apply a soft-delete filter according to the architectural standard.
        // 0 indicates the record is active.
        builder.HasQueryFilter(cc => cc.GcRecord == 0);
    }
}
