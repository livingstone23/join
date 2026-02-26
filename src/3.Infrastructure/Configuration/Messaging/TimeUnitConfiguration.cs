// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Infrastructure.Configuration.Messaging;

/// <summary>
/// Configures the database mapping for the <see cref="TimeUnit"/> entity.
/// This setup defines the table for time measurements (e.g., Hours, Days)
/// used in SLA calculations and time tracking.
/// </summary>
public class TimeUnitConfiguration : IEntityTypeConfiguration<TimeUnit>
{
    /// <summary>
    /// Configures the <see cref="TimeUnit"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<TimeUnit> builder)
    {
        // Sets the primary key for the entity.
        builder.HasKey(p => p.Id);

        // Configures the 'Name' property: it is required and has a maximum length of 50 characters.
        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(50);

        // Configures the 'Code' property: it is required.
        builder.Property(p => p.Code)
            .IsRequired();
        
        // --- Relationships ---

        // Configures the one-to-many relationship with Ticket.
        // A TimeUnit can be associated with many Tickets.
        builder.HasMany(p => p.Tickets)
            .WithOne(t => t.TimeUnit)
            .HasForeignKey(t => t.TimeUnitId);

        // Maps the entity to the "TimeUnits" table in the "Messaging" schema.
        builder.ToTable("TimeUnits", "Messaging");
    }
}
