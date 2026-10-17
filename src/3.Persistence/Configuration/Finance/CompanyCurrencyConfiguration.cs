// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Finance;

/// <summary>
/// Configures the currencies a company uses (<c>Finance.CompanyCurrencies</c>, SPEC 56).
/// </summary>
public class CompanyCurrencyConfiguration : IEntityTypeConfiguration<CompanyCurrency>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CompanyCurrency> builder)
    {
        builder.ToTable("CompanyCurrencies", "Finance");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.IsBase).IsRequired();
        builder.Property(c => c.IsActive).IsRequired().HasDefaultValue(true);

        builder.HasOne(c => c.Company)
            .WithMany()
            .HasForeignKey(c => c.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Currency)
            .WithMany()
            .HasForeignKey(c => c.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.CompanyId, c.CurrencyId })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_CompanyCurrencies_Company_Currency");

        // Rule 1: exactly one live base currency per company.
        builder.HasIndex(c => c.CompanyId)
            .IsUnique()
            .HasFilter("\"isbase\" = TRUE AND \"gcrecord\" = 0")
            .HasDatabaseName("UX_CompanyCurrencies_Company_Base");
    }
}
