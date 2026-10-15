// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Customers;

/// <summary>
/// Configures the database mapping for the <see cref="Customer"/> entity (schema <c>Customer</c>, SPEC 55).
/// </summary>
public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers", "Customer");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CustomerCode)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(c => c.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(c => c.ActivatedAt)
            .IsRequired();

        builder.Property(c => c.DeactivatedAt)
            .IsRequired(false);

        builder.Property(c => c.GcRecord)
            .IsRequired()
            .HasDefaultValue(0);

        builder.HasOne(c => c.Company)
            .WithMany()
            .HasForeignKey(c => c.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Person)
            .WithMany()
            .HasForeignKey(c => c.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.CustomerStage)
            .WithMany()
            .HasForeignKey(c => c.CustomerStageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.CompanyId, c.CustomerCode })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_Customers_Company_CustomerCode");

        // One customer per person and company, live or not (SPEC 55 rule 1): no filter on purpose.
        builder.HasIndex(c => new { c.CompanyId, c.PersonId })
            .IsUnique()
            .HasDatabaseName("UX_Customers_Company_Person");

        builder.HasIndex(c => new { c.CompanyId, c.CustomerStageId })
            .HasDatabaseName("IX_Customers_Company_Stage");

        builder.HasQueryFilter(c => c.GcRecord == 0);
    }
}
