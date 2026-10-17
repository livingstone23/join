// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Customers.Leads;

/// <summary>Configures <c>Customer.LeadDocuments</c> (SPEC 62).</summary>
public class LeadDocumentConfiguration : IEntityTypeConfiguration<LeadDocument>
{
    public void Configure(EntityTypeBuilder<LeadDocument> builder)
    {
        builder.ToTable("LeadDocuments", "Customer");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DocumentType).IsRequired();
        builder.Property(x => x.OriginalName).IsRequired().HasMaxLength(150);
        builder.Property(x => x.NewName).IsRequired().HasMaxLength(150);
        builder.Property(x => x.Path).IsRequired().HasMaxLength(500);
        builder.Property(x => x.StorageProvider).IsRequired();
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(150);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LeadInteraction>().WithMany().HasForeignKey(x => x.LeadInteractionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.LeadId, x.Created }).HasDatabaseName("IX_LeadDocuments_Lead_Created");
        builder.HasIndex(x => new { x.CompanyId, x.Created }).HasDatabaseName("IX_LeadDocuments_Company_Created");
        builder.HasIndex(x => x.LeadInteractionId).HasDatabaseName("IX_LeadDocuments_Interaction");
    }
}

/// <summary>Configures <c>Customer.LeadAttachmentSettings</c>, one live row per company (SPEC 62).</summary>
public class LeadAttachmentSettingsConfiguration : IEntityTypeConfiguration<LeadAttachmentSettings>
{
    public void Configure(EntityTypeBuilder<LeadAttachmentSettings> builder)
    {
        builder.ToTable("LeadAttachmentSettings", "Customer");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AllowedDocumentTypes).IsRequired();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CompanyId).IsUnique().HasFilter("\"gcrecord\" = 0").HasDatabaseName("UX_LeadAttachmentSettings_Company");
    }
}
