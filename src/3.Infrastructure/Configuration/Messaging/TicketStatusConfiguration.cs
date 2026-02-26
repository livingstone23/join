// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Infrastructure.Configuration.Messaging;

/// <summary>
/// Configures the database mapping for the <see cref="TicketStatus"/> entity.
/// This setup defines the table structure, constraints, and relationships
/// for the ticket status data in the persistence layer.
/// </summary>
public class TicketStatusConfiguration : IEntityTypeConfiguration<TicketStatus>
{
    /// <summary>
    /// Configures the <see cref="TicketStatus"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<TicketStatus> builder)
    {
        // Sets the primary key for the entity.
        builder.HasKey(p => p.Id);

        // Configures the 'Name' property: it is required and has a maximum length of 50 characters.
        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(50);

        // Configures the 'Description' property: it is required and has a maximum length of 200 characters.
        builder.Property(p => p.Description)
            .IsRequired()
            .HasMaxLength(200);

        // Maps the entity to the "TicketStatuses" table in the "Messaging" schema.
        builder.ToTable("TicketStatuses", "Messaging");
    }
}
