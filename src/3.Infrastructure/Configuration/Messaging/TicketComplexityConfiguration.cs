// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Infrastructure.Configuration.Messaging;



/// <summary>
/// Configures the database mapping for the <see cref="TicketComplexity"/> entity.
/// This setup defines the table structure, constraints, and relationships
/// for the ticket complexity data, which includes SLA metrics.
/// </summary>
public class TicketComplexityConfiguration : IEntityTypeConfiguration<TicketComplexity>
{
    public void Configure(EntityTypeBuilder<TicketComplexity> builder)
    {
        builder.ToTable("TicketComplexities", "Messaging");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Description).HasMaxLength(200);
        builder.Property(p => p.Code).IsRequired();
        builder.Property(p => p.ResolutionTimeUnits).IsRequired();
            
        // --- Relationships ---

        // Defines a one-to-many relationship with TimeUnit.
        builder.HasOne(p => p.TimeUnit)
            .WithMany(t => t.TicketComplexities) // CORREGIDO: Evita shadow property TimeUnitId1
            .HasForeignKey(p => p.TimeUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(a => a.GcRecord == 0);
    }
}
