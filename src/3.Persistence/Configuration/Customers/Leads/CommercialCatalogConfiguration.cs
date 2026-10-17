// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Audit;
using JOIN.Domain.Customers.Leads;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Customers.Leads;

/// <summary>
/// Shared mapping of the per-company commercial catalogs (SPEC 59): <c>Customer</c> schema, code and name unique among
/// the company's live rows.
/// </summary>
internal static class CommercialCatalogConfiguration
{
    internal static void ConfigureCatalog<T>(EntityTypeBuilder<T> builder, string table) where T : BaseTenantEntity, ICommercialCatalogItem
    {
        builder.ToTable(table, "Customer");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).IsRequired().HasMaxLength(30);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.IsActive).IsRequired();

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CompanyId, x.Code })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName($"UX_{table}_Company_Code");

        builder.HasIndex(x => new { x.CompanyId, x.Name })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName($"UX_{table}_Company_Name");
    }
}

/// <summary>Configures <c>Customer.LeadStages</c> (SPEC 59).</summary>
public class LeadStageConfiguration : IEntityTypeConfiguration<LeadStage>
{
    public void Configure(EntityTypeBuilder<LeadStage> builder)
    {
        CommercialCatalogConfiguration.ConfigureCatalog(builder, "LeadStages");
        builder.Property(x => x.Probability).IsRequired();
        builder.Property(x => x.Kind).IsRequired();
        builder.Ignore(x => x.IsSystem);
        builder.Ignore(x => x.IsUniqueKind);

        // Rule 2: exactly one live Initial (1), Won (3) and Lost (4) stage per company.
        builder.HasIndex(x => x.CompanyId).IsUnique().HasFilter("\"kind\" = 1 AND \"gcrecord\" = 0").HasDatabaseName("UX_LeadStages_Company_Initial");
        builder.HasIndex(x => new { x.CompanyId, x.Kind }).IsUnique().HasFilter("\"kind\" IN (3, 4) AND \"gcrecord\" = 0").HasDatabaseName("UX_LeadStages_Company_WonLost");
    }
}

/// <summary>Configures <c>Customer.LeadSources</c> (SPEC 59).</summary>
public class LeadSourceConfiguration : IEntityTypeConfiguration<LeadSource>
{
    public void Configure(EntityTypeBuilder<LeadSource> builder)
    {
        CommercialCatalogConfiguration.ConfigureCatalog(builder, "LeadSources");
        builder.Property(x => x.Description).HasMaxLength(250);
    }
}

/// <summary>Configures <c>Customer.LeadReasons</c> (SPEC 59).</summary>
public class LeadReasonConfiguration : IEntityTypeConfiguration<LeadReason>
{
    public void Configure(EntityTypeBuilder<LeadReason> builder)
    {
        CommercialCatalogConfiguration.ConfigureCatalog(builder, "LeadReasons");
        builder.Property(x => x.UsedFor).IsRequired();
    }
}

/// <summary>Configures <c>Customer.RelationshipTypes</c> (SPEC 59).</summary>
public class RelationshipTypeConfiguration : IEntityTypeConfiguration<RelationshipType>
{
    public void Configure(EntityTypeBuilder<RelationshipType> builder)
    {
        CommercialCatalogConfiguration.ConfigureCatalog(builder, "RelationshipTypes");
        builder.Property(x => x.OnWin).IsRequired();
    }
}

/// <summary>Configures the global <c>Customer.ContactTypes</c> master (SPEC 59).</summary>
public class ContactTypeConfiguration : IEntityTypeConfiguration<ContactType>
{
    public void Configure(EntityTypeBuilder<ContactType> builder)
    {
        builder.ToTable("ContactTypes", "Customer");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(30);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Icon).IsRequired().HasMaxLength(50);
        builder.Property(x => x.CommunicationChannelCode).HasMaxLength(30);
        builder.Property(x => x.IsActive).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique().HasFilter("\"gcrecord\" = 0").HasDatabaseName("UX_ContactTypes_Code");
    }
}

/// <summary>Configures <c>Customer.ContactTypeCompanies</c> (SPEC 59).</summary>
public class ContactTypeCompanyConfiguration : IEntityTypeConfiguration<ContactTypeCompany>
{
    public void Configure(EntityTypeBuilder<ContactTypeCompany> builder)
    {
        builder.ToTable("ContactTypeCompanies", "Customer");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.IsActive).IsRequired();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ContactType).WithMany().HasForeignKey(x => x.ContactTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CompanyId, x.ContactTypeId }).IsUnique().HasFilter("\"gcrecord\" = 0").HasDatabaseName("UX_ContactTypeCompanies_Company_Type");
    }
}

/// <summary>Configures <c>Customer.LeadCompanySettings</c>, one live row per company (SPEC 59).</summary>
public class LeadCompanySettingsConfiguration : IEntityTypeConfiguration<LeadCompanySettings>
{
    public void Configure(EntityTypeBuilder<LeadCompanySettings> builder)
    {
        builder.ToTable("LeadCompanySettings", "Customer");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DuplicateOpenLeadPolicy).IsRequired();
        builder.Property(x => x.CodePrefix).IsRequired().HasMaxLength(10);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CompanyId).IsUnique().HasFilter("\"gcrecord\" = 0").HasDatabaseName("UX_LeadCompanySettings_Company");
    }
}
