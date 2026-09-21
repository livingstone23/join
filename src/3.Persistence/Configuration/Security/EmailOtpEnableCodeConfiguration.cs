// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Security;

/// <summary>
/// EF mapping for <see cref="EmailOtpEnableCode"/>. Exact clone of
/// <see cref="PhoneVerificationCodeConfiguration"/>'s shape, but for the authenticated
/// email-otp enable/disable flow.
/// </summary>
public class EmailOtpEnableCodeConfiguration : IEntityTypeConfiguration<EmailOtpEnableCode>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailOtpEnableCode> builder)
    {
        builder.ToTable("EmailOtpEnableCodes", "Security");

        builder.HasKey(code => code.Id);

        builder.Property(code => code.Email)
            .IsRequired()
            .HasMaxLength(256);

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

        // Backs GetLatestActiveByUserAsync — same shape as PhoneVerificationCodes.
        builder.HasIndex(code => new { code.UserId, code.ExpiresAtUtc })
            .HasDatabaseName("IX_EmailOtpEnableCodes_UserId_ExpiresAtUtc")
            .IsDescending(false, true);
    }
}
