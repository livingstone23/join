// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Customers;

/// <summary>
/// Configures the per-company customer stage catalog (<c>Customer.CustomerStages</c>, SPEC 55 part F).
/// </summary>
public class CustomerStageConfiguration : IEntityTypeConfiguration<CustomerStage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CustomerStage> builder)
    {
        builder.ToTable("CustomerStages", "Customer");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.Description)
            .HasMaxLength(250);

        builder.Property(s => s.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasOne(s => s.Company)
            .WithMany()
            .HasForeignKey(s => s.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.CompanyId, s.Code })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_CustomerStages_Company_Code");

        builder.HasIndex(s => new { s.CompanyId, s.Name })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_CustomerStages_Company_Name");

        // Exactly one live default stage per company (rule 8).
        builder.HasIndex(s => s.CompanyId)
            .IsUnique()
            .HasFilter("\"isdefault\" = TRUE AND \"gcrecord\" = 0")
            .HasDatabaseName("UX_CustomerStages_Company_Default");

        builder.HasQueryFilter(s => s.GcRecord == 0);
    }
}
