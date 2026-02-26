// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Infrastructure.Configuration.Messaging;

/// <summary>
/// Configures the database mapping for the <see cref="UserCommunicationChannel"/> entity.
/// This setup defines the join table linking users to their specific communication channels
/// and identifiers, such as a phone number for WhatsApp.
/// </summary>
public class UserCommunicationChannelConfiguration : IEntityTypeConfiguration<UserCommunicationChannel>
{
    /// <summary>
    /// Configures the <see cref="UserCommunicationChannel"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<UserCommunicationChannel> builder)
    {
        // Sets the primary key for the entity.
        builder.HasKey(p => p.Id);

        // Configures the 'ChannelIdentifier' property: it is required and has a maximum length of 100 characters.
        builder.Property(p => p.ChannelIdentifier)
            .IsRequired()
            .HasMaxLength(100);

        // --- Relationships ---

        // Defines a one-to-many relationship with ApplicationUser.
        // A UserCommunicationChannel belongs to one User.
        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade); // Deleting a user also deletes their channel mappings.

        // Defines a one-to-many relationship with CommunicationChannel.
        // A UserCommunicationChannel belongs to one Channel.
        builder.HasOne(p => p.Channel)
            .WithMany()
            .HasForeignKey(p => p.CommunicationChannelId)
            .OnDelete(DeleteBehavior.Restrict); // Prevents deleting a channel if it's in use by a user.

        // --- Indexes ---

        // Creates a unique index on UserId and CommunicationChannelId to ensure
        // a user can only be mapped to a specific channel once.
        builder.HasIndex(p => new { p.UserId, p.CommunicationChannelId }).IsUnique();

        // Maps the entity to the "UserCommunicationChannels" table in the "Messaging" schema.
        builder.ToTable("UserCommunicationChannels", "Messaging");
    }
}
