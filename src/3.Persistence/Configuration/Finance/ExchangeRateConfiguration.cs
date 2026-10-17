// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Finance;

/// <summary>
/// Configures the dated exchange rates of a company (<c>Finance.ExchangeRates</c>, SPEC 56).
/// </summary>
public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("ExchangeRates", "Finance");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RateDate).IsRequired();
        builder.Property(r => r.Rate).IsRequired().HasPrecision(18, 6);

        builder.HasOne(r => r.Company)
            .WithMany()
            .HasForeignKey(r => r.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Currency)
            .WithMany()
            .HasForeignKey(r => r.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.CompanyId, r.CurrencyId, r.RateDate })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_ExchangeRates_Company_Currency_Date");
    }
}
