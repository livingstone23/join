// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Security;

/// <summary>
/// EF mapping for <see cref="UserMfaRecoveryCode"/>. Soft-delete follows the project
/// convention (<c>GcRecord = 0</c> for live rows) because recovery codes live or die
/// per user and their generation lifecycle.
/// </summary>
public class UserMfaRecoveryCodeConfiguration : IEntityTypeConfiguration<UserMfaRecoveryCode>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserMfaRecoveryCode> builder)
    {
        builder.ToTable("UserMfaRecoveryCodes", "Security");

        builder.HasKey(code => code.Id);

        builder.Property(code => code.CodeHash)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(code => code.CreatedAtUtc)
            .IsRequired();

        builder.Property(code => code.UsedAtUtc);

        builder.HasOne(code => code.User)
            .WithMany()
            .HasForeignKey(code => code.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Drives DisableMfa recovery-code path lookup + MFA-management UX listings.
        builder.HasIndex(code => new { code.UserId, code.UsedAtUtc })
            .HasDatabaseName("IX_UserMfaRecoveryCodes_UserId_UsedAtUtc");
    }
}
