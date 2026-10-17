// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Catalog;

/// <summary>
/// Configures the product categories of a company (<c>Catalog.ProductCategories</c>, SPEC 57).
/// </summary>
public class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("ProductCategories", "Catalog");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.IsActive).IsRequired().HasDefaultValue(true);

        builder.HasOne(c => c.Company)
            .WithMany()
            .HasForeignKey(c => c.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Parent)
            .WithMany()
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Convention 3: two root categories (ParentId NULL) with the same name must collide too.
        builder.HasIndex(c => new { c.CompanyId, c.ParentId, c.Name })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .AreNullsDistinct(false)
            .HasDatabaseName("UX_ProductCategories_Company_Parent_Name");
    }
}
