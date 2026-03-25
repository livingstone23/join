// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Security;

/// <summary>
/// Configures the database mapping for the <see cref="UserConnectionLog"/> entity.
/// Defines table structure and relationships for tracking user login events.
/// </summary>
public class UserConnectionLogConfiguration : IEntityTypeConfiguration<UserConnectionLog>
{
    /// <summary>
    /// Configures the <see cref="UserConnectionLog"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<UserConnectionLog> builder)
    {
        // Map to table "UserConnectionLogs" in schema "Security"
        builder.ToTable("UserConnectionLogs", "Security");

        // Set the primary key
        builder.HasKey(log => log.Id);

        // --- Properties ---
        builder.Property(log => log.IpAddress)
            .IsRequired()
            .HasMaxLength(45); // Sufficient for IPv4 or IPv6 addresses.

        builder.Property(log => log.Country)
            .HasMaxLength(100);

        builder.Property(log => log.UserAgent)
            .HasMaxLength(500);

        builder.Property(log => log.ConnectionDate)
            .IsRequired();

        builder.Property(log => log.IsActiveSession)
            .IsRequired();

        // --- Relationships ---

        // Required relationship with ApplicationUser
        // A log entry belongs to one user.
        builder.HasOne(log => log.User)
            .WithMany() // A user can have many connection logs.
            .HasForeignKey(log => log.UserId)
            .OnDelete(DeleteBehavior.Cascade); // If the user is deleted, their logs are also deleted.

        // --- Indexes ---

        // Index on UserId to improve query performance when retrieving a user's logs.
        builder.HasIndex(log => log.UserId);

        // Index on IsActiveSession to quickly find active sessions.
        builder.HasIndex(log => log.IsActiveSession);
    }
}
