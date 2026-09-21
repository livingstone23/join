// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Security;

/// <summary>
/// EF mapping for <see cref="MfaLoginChallenge"/>. Soft-delete/invalidation follows the
/// project convention (<c>GcRecord = 0</c>); the token-hash index backs the
/// "active challenge by opaque token" lookup used by <c>send</c>/<c>verify</c>.
/// </summary>
public class MfaLoginChallengeConfiguration : IEntityTypeConfiguration<MfaLoginChallenge>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MfaLoginChallenge> builder)
    {
        builder.ToTable("MfaLoginChallenges", "Security");

        builder.HasKey(challenge => challenge.Id);

        builder.Property(challenge => challenge.TokenHash)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(challenge => challenge.ExpiresAtUtc)
            .IsRequired();

        builder.Property(challenge => challenge.AttemptCount)
            .IsRequired();

        builder.Property(challenge => challenge.EmailCodeHash)
            .HasMaxLength(500);

        builder.HasOne(challenge => challenge.User)
            .WithMany()
            .HasForeignKey(challenge => challenge.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Backs GetActiveByTokenHashAsync — one active challenge per opaque token.
        builder.HasIndex(challenge => challenge.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_MfaLoginChallenges_TokenHash");

        builder.HasIndex(challenge => new { challenge.UserId, challenge.ExpiresAtUtc })
            .HasDatabaseName("IX_MfaLoginChallenges_UserId_ExpiresAtUtc");
    }
}
