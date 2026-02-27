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
    /// <summary>
    /// Configures the <see cref="TicketComplexity"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<TicketComplexity> builder)
    {

        // Maps the entity to the "TicketComplexities" table in the "Messaging" schema.
        builder.ToTable("TicketComplexities", "Messaging");

        // Sets the primary key for the entity.
        builder.HasKey(p => p.Id);

        // Configures the 'Name' property: it is required and has a maximum length of 50 characters.
        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(50);

        // Configures the 'Description' property: it is optional and has a maximum length of 200 characters.
        builder.Property(p => p.Description)
            .HasMaxLength(200);

        // Configures the 'Code' property: it is required.
        builder.Property(p => p.Code)
            .IsRequired();

        // Configures the 'ResolutionTimeUnits' property: it is required.
        builder.Property(p => p.ResolutionTimeUnits)
            .IsRequired();
            
        // --- Relationships ---

        // Defines a one-to-many relationship with TimeUnit.
        // A TicketComplexity has one TimeUnit, but a TimeUnit can be associated with many TicketComplexities.
        builder.HasOne(p => p.TimeUnit)
            .WithMany()
            .HasForeignKey(p => p.TimeUnitId)
            .OnDelete(DeleteBehavior.Restrict); // Prevents deleting a TimeUnit if it's in use.

        // Apply a soft-delete filter to automatically exclude records marked as deleted.
        builder.HasQueryFilter(a => a.GcRecord == 0);
        
        
    }
}
