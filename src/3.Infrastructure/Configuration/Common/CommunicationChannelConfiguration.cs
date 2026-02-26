// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Infrastructure.Configuration.Common;

/// <summary>
/// Configures the database mapping for the <see cref="CommunicationChannel"/> entity.
/// Defines the table for the catalog of communication platforms (e.g., WhatsApp, Telegram).
/// </summary>
public class CommunicationChannelConfiguration : IEntityTypeConfiguration<CommunicationChannel>
{
    /// <summary>
    /// Configures the <see cref="CommunicationChannel"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<CommunicationChannel> builder)
    {
        // Map to table "CommunicationChannels" in schema "Common"
        builder.ToTable("CommunicationChannels", "Common");

        // Set the primary key
        builder.HasKey(cc => cc.Id);

        // --- Properties ---

        builder.Property(cc => cc.Name).IsRequired().HasMaxLength(100);
        builder.Property(cc => cc.Provider).HasMaxLength(100);
        builder.Property(cc => cc.Code).HasMaxLength(50);
        builder.Property(cc => cc.IsActive).IsRequired();

        // --- Relationships ---
        
        // One-to-many relationship with Ticket
        builder.HasMany(cc => cc.CreatedTickets)
            .WithOne(t => t.Channel)
            .HasForeignKey(t => t.ChannelId);

        // --- Indexes ---

        // Unique index on the 'Name' property for faster lookups and to prevent duplicates.
        builder.HasIndex(cc => cc.Name).IsUnique();

        // --- Query Filters ---

        // Apply a soft-delete filter.
        builder.HasQueryFilter(cc => cc.GcRecord == 0);
    }
}
