// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Customers;

/// <summary>
/// Configures the insert-only customer status history (<c>Customer.CustomerStatusHistory</c>, SPEC 55 part C).
/// </summary>
public class CustomerStatusHistoryConfiguration : IEntityTypeConfiguration<CustomerStatusHistory>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CustomerStatusHistory> builder)
    {
        builder.ToTable("CustomerStatusHistory", "Customer");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Action)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(h => h.Source)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(h => h.Notes)
            .HasMaxLength(500);

        builder.Property(h => h.OccurredAt)
            .IsRequired();

        builder.HasOne(h => h.Company)
            .WithMany()
            .HasForeignKey(h => h.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.Customer)
            .WithMany()
            .HasForeignKey(h => h.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.User)
            .WithMany()
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => new { h.CustomerId, h.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_CustomerStatusHistory_Customer_OccurredAt");

        builder.HasQueryFilter(h => h.GcRecord == 0);
    }
}
