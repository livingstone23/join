// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Admin;

/// <summary>
/// Configures the database mapping for the <see cref="Customer"/> entity.
/// </summary>
public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers", "Admin");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CustomerCode)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(c => c.PersonLifecycleStage)
            .IsRequired()
            .HasConversion<int>();

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

        builder.HasIndex(c => new { c.CompanyId, c.CustomerCode, c.GcRecord })
            .IsUnique()
            .HasDatabaseName("IX_Customers_Company_CustomerCode_GcRecord");

        builder.HasIndex(c => new { c.CompanyId, c.PersonId, c.UserId, c.GcRecord })
            .IsUnique()
            .HasDatabaseName("IX_Customers_Company_Person_User_GcRecord");

        builder.HasIndex(c => new { c.CompanyId, c.PersonLifecycleStage })
            .HasDatabaseName("IX_Customers_Company_PersonLifecycleStage");

        builder.HasQueryFilter(c => c.GcRecord == 0);
    }
}
