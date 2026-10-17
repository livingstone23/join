// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Finance;

/// <summary>
/// Configures the global currency catalog (<c>Finance.Currencies</c>, SPEC 56).
/// </summary>
public class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("Currencies", "Finance");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code).IsRequired().HasMaxLength(3);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Symbol).IsRequired().HasMaxLength(10);
        builder.Property(c => c.Decimals).IsRequired();
        builder.Property(c => c.IsActive).IsRequired().HasDefaultValue(true);

        builder.HasIndex(c => c.Code)
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_Currencies_Code");
    }
}
