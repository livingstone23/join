// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Security;

/// <summary>
/// EF mapping for <see cref="PhoneVerificationCode"/>. Soft-delete follows the project
/// convention (<c>GcRecord = 0</c>); the lookup index is built so the per-user
/// "latest active" query stays cheap.
/// </summary>
public class PhoneVerificationCodeConfiguration : IEntityTypeConfiguration<PhoneVerificationCode>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PhoneVerificationCode> builder)
    {
        builder.ToTable("PhoneVerificationCodes", "Security");

        builder.HasKey(code => code.Id);

        builder.Property(code => code.PhoneNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(code => code.CodeHash)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(code => code.ExpiresAtUtc)
            .IsRequired();

        builder.Property(code => code.AttemptCount)
            .IsRequired();

        builder.HasOne(code => code.User)
            .WithMany()
            .HasForeignKey(code => code.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Spec F8 acceptance: drives the "latest active code for a user" lookup.
        builder.HasIndex(code => new { code.UserId, code.ExpiresAtUtc })
            .HasDatabaseName("IX_PhoneVerificationCodes_UserId_ExpiresAtUtc")
            .IsDescending(false, true);
    }
}
