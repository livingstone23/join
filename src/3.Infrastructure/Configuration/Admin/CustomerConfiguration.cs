// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Infrastructure.Configuration.Admin;

/// <summary>
/// Configures the database mapping for the <see cref="Customer"/> entity.
/// Defines table structure, constraints, and relationships for the core CRM entity.
/// </summary>
public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    /// <summary>
    /// Configures the <see cref="Customer"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        // Map to table "Customers" in schema "Admin"
        builder.ToTable("Customers", "Admin");

        // Set the primary key
        builder.HasKey(c => c.Id);

        // --- Properties ---
        
        // PersonType is an enum, configured to be stored as a string.
        builder.Property(c => c.PersonType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(c => c.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(c => c.MiddleName).HasMaxLength(100);
        builder.Property(c => c.LastName).IsRequired().HasMaxLength(100);
        builder.Property(c => c.SecondLastName).HasMaxLength(100);
        builder.Property(c => c.CommercialName).HasMaxLength(200);
        builder.Property(c => c.IdentificationNumber).IsRequired().HasMaxLength(50);

        // --- Relationships ---

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

        // One-to-many relationship with CustomerAddress
        builder.HasMany(c => c.Addresses)
            .WithOne(ca => ca.Customer)
            .HasForeignKey(ca => ca.CustomerId);

        // One-to-many relationship with CustomerContact
        builder.HasMany(c => c.Contacts)
            .WithOne(cc => cc.Customer)
            .HasForeignKey(cc => cc.CustomerId);
            
        // One-to-many relationship with Ticket
        builder.HasMany(c => c.Tickets)
            .WithOne(t => t.Customer)
            .HasForeignKey(t => t.CustomerId);

        // --- Indexes ---

        // Unique index to ensure a customer's identification number is unique within a company.
        builder.HasIndex(c => new { c.CompanyId, c.IdentificationNumber }).IsUnique();
        
        // --- Query Filters ---

        // Apply a soft-delete filter.
        builder.HasQueryFilter(c => c.GcRecord == 0);
    }
}
