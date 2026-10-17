// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using JOIN.Domain.Catalog;
using JOIN.Domain.Customers;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Customers.Teams;
using JOIN.Domain.Finance;
using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Customers.Leads;

/// <summary>Configures <c>Customer.Leads</c> (SPEC 61) with the inbox, board and person indexes.</summary>
public class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("Leads", "Customer");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(30);
        builder.Property(x => x.Topic).IsRequired().HasMaxLength(200);
        builder.Property(x => x.StatusNotes).HasMaxLength(1000);
        builder.Property(x => x.ExchangeRate).HasPrecision(18, 6);
        builder.Property(x => x.ExchangeRateDate).HasColumnType("date");
        builder.Property(x => x.EstimatedValue).HasPrecision(18, 2);
        builder.Property(x => x.EstimatedValueBase).HasPrecision(18, 2);
        builder.Property(x => x.EstimatedCloseDate).HasColumnType("date");
        builder.Property(x => x.WonValue).HasPrecision(18, 2);
        builder.Property(x => x.WonExchangeRate).HasPrecision(18, 6);
        builder.Property(x => x.WonValueBase).HasPrecision(18, 2);
        builder.Property(x => x.CompetitorName).HasMaxLength(200);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(x => x.ReferredByPersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesTeam>().WithMany().HasForeignKey(x => x.SalesTeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LeadStage>().WithMany().HasForeignKey(x => x.LeadStageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LeadSource>().WithMany().HasForeignKey(x => x.LeadSourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RelationshipType>().WithMany().HasForeignKey(x => x.RelationshipTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LeadReason>().WithMany().HasForeignKey(x => x.LeadReasonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Area>().WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("\"gcrecord\" = 0").HasDatabaseName("UX_Leads_Company_Code");
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.LeadStageId }).HasDatabaseName("IX_Leads_Company_Status_Stage");
        builder.HasIndex(x => new { x.CompanyId, x.OwnerUserId, x.Status }).HasDatabaseName("IX_Leads_Company_Owner_Status");
        builder.HasIndex(x => new { x.CompanyId, x.SalesTeamId, x.Status }).HasDatabaseName("IX_Leads_Company_Team_Status");
        builder.HasIndex(x => new { x.CompanyId, x.PersonId, x.Status }).HasDatabaseName("IX_Leads_Company_Person_Status");
    }
}

/// <summary>Configures <c>Customer.LeadStageHistory</c> (SPEC 61).</summary>
public class LeadStageHistoryConfiguration : IEntityTypeConfiguration<LeadStageHistory>
{
    public void Configure(EntityTypeBuilder<LeadStageHistory> builder)
    {
        builder.ToTable("LeadStageHistory", "Customer");
        builder.HasKey(x => x.Id);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LeadStage>().WithMany().HasForeignKey(x => x.FromStageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LeadStage>().WithMany().HasForeignKey(x => x.ToStageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.LeadId, x.ChangedAt }).HasDatabaseName("IX_LeadStageHistory_Lead_ChangedAt");
    }
}

/// <summary>Configures <c>Customer.LeadOwnershipHistory</c> (SPEC 61).</summary>
public class LeadOwnershipHistoryConfiguration : IEntityTypeConfiguration<LeadOwnershipHistory>
{
    public void Configure(EntityTypeBuilder<LeadOwnershipHistory> builder)
    {
        builder.ToTable("LeadOwnershipHistory", "Customer");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.LeadId, x.At }).HasDatabaseName("IX_LeadOwnershipHistory_Lead_At");
    }
}

/// <summary>Configures <c>Customer.LeadStatusHistory</c> (SPEC 61).</summary>
public class LeadStatusHistoryConfiguration : IEntityTypeConfiguration<LeadStatusHistory>
{
    public void Configure(EntityTypeBuilder<LeadStatusHistory> builder)
    {
        builder.ToTable("LeadStatusHistory", "Customer");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LeadReason>().WithMany().HasForeignKey(x => x.LeadReasonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.LeadId, x.At }).HasDatabaseName("IX_LeadStatusHistory_Lead_At");
    }
}

/// <summary>Configures <c>Customer.LeadInteractions</c> (SPEC 61).</summary>
public class LeadInteractionConfiguration : IEntityTypeConfiguration<LeadInteraction>
{
    public void Configure(EntityTypeBuilder<LeadInteraction> builder)
    {
        builder.ToTable("LeadInteractions", "Customer");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Summary).IsRequired().HasMaxLength(2000);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ContactTypeCompany>().WithMany().HasForeignKey(x => x.ContactTypeCompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JOIN.Domain.Calendars.CalendarEvent>().WithMany().HasForeignKey(x => x.NextActionEventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CompanyId, x.LeadId, x.OccurredAt }).HasDatabaseName("IX_LeadInteractions_Company_Lead_OccurredAt");
    }
}

/// <summary>Configures <c>Customer.LeadComments</c> (SPEC 61).</summary>
public class LeadCommentConfiguration : IEntityTypeConfiguration<LeadComment>
{
    public void Configure(EntityTypeBuilder<LeadComment> builder)
    {
        builder.ToTable("LeadComments", "Customer");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Body).IsRequired().HasMaxLength(2000);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LeadInteraction>().WithMany().HasForeignKey(x => x.LeadInteractionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.LeadId, x.Created }).HasDatabaseName("IX_LeadComments_Lead_Created");
    }
}

/// <summary>Configures <c>Customer.LeadProducts</c> (SPEC 61).</summary>
public class LeadProductConfiguration : IEntityTypeConfiguration<LeadProduct>
{
    public void Configure(EntityTypeBuilder<LeadProduct> builder)
    {
        builder.ToTable("LeadProducts", "Customer");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 2);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.DiscountPercent).HasPrecision(5, 2);
        builder.Property(x => x.LineTotal).HasPrecision(18, 2);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LeadInteraction>().WithMany().HasForeignKey(x => x.OfferedInInteractionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.LeadId, x.Status }).HasDatabaseName("IX_LeadProducts_Lead_Status");
        builder.HasIndex(x => x.ProductId).HasDatabaseName("IX_LeadProducts_Product");
    }
}
