using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Security;

/// <summary>
/// Configures the database mapping for the <see cref="UserRefreshToken"/> entity.
/// </summary>
public class UserRefreshTokenConfiguration : IEntityTypeConfiguration<UserRefreshToken>
{
    /// <summary>
    /// Applies the persistence rules for the refresh token entity.
    /// </summary>
    /// <param name="builder">The entity builder used to configure the mapping.</param>
    public void Configure(EntityTypeBuilder<UserRefreshToken> builder)
    {
        builder.ToTable("UserRefreshTokens", "Security");

        builder.HasKey(token => token.Id);

        builder.Property(token => token.Token)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(token => token.ExpiryDate)
            .IsRequired();

        builder.Property(token => token.IsRevoked)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(token => token.Token)
            .IsUnique();

        builder.HasIndex(token => new { token.UserId, token.IsRevoked });

        builder.HasOne(token => token.User)
            .WithMany(user => user.RefreshTokens)
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
