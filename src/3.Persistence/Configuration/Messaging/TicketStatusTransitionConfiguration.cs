// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Messaging;



/// <summary>
/// Configures the database mapping for the <see cref="TicketStatusTransition"/> entity.
/// Holds the optional per-company rules that restrict transitions between <see cref="TicketStatus"/> values.
/// </summary>
public class TicketStatusTransitionConfiguration : IEntityTypeConfiguration<TicketStatusTransition>
{
    /// <summary>
    /// Configures the <see cref="TicketStatusTransition"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<TicketStatusTransition> builder)
    {
        builder.ToTable("TicketStatusTransitions", "Messaging");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.CompanyId)
            .IsRequired();

        builder.Property(p => p.FromStatusId)
            .IsRequired();

        builder.Property(p => p.ToStatusId)
            .IsRequired();

        // Explicit Company FK to avoid relying on the EF convention and to match
        // the same pattern used by TicketComplexityConfiguration.
        builder.HasOne(p => p.Company)
            .WithMany()
            .HasForeignKey(p => p.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Two FKs to TicketStatus. Use a separate WithMany() for each one so EF
        // does not synthesize an ambiguous navigation on TicketStatus
        // (two HasOne with .WithMany() empty, each with its own HasForeignKey).
        builder.HasOne(p => p.FromStatus)
            .WithMany()
            .HasForeignKey(p => p.FromStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.ToStatus)
            .WithMany()
            .HasForeignKey(p => p.ToStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        // Uniqueness of (CompanyId, FromStatusId, ToStatusId) for active rows only —
        // the rule's identity is the tuple, see SPEC 37 decisions.
        builder.HasIndex(p => new { p.CompanyId, p.FromStatusId, p.ToStatusId })
            .HasDatabaseName("UX_TicketStatusTransitions_Company_From_To")
            .IsUnique()
            .HasFilter("[GcRecord] = 0");

        builder.HasQueryFilter(a => a.GcRecord == 0);
    }
}