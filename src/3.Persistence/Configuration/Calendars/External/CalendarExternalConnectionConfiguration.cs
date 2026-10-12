using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars.External;

/// <summary>
/// Configures the external account linked by each JOIN user (<c>Calendar.ExternalConnections</c>,
/// SPEC 46-post). Every FK is <see cref="DeleteBehavior.Restrict"/>; the unique-active index lets the
/// user re-link after a revocation.
/// </summary>
public sealed class CalendarExternalConnectionConfiguration : IEntityTypeConfiguration<CalendarExternalConnection>
{
    public void Configure(EntityTypeBuilder<CalendarExternalConnection> builder)
    {
        builder.ToTable("ExternalConnections", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExternalAccountEmail).IsRequired().HasMaxLength(256);
        builder.Property(x => x.ExternalCalendarId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.EncryptedRefreshToken).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.GrantedScopes).IsRequired().HasMaxLength(500);
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.Property(x => x.InboundSyncToken).HasMaxLength(500);
        builder.Property(x => x.WatchChannelId).HasMaxLength(200);
        builder.Property(x => x.WatchResourceId).HasMaxLength(200);
        builder.Property(x => x.WatchChannelToken).HasMaxLength(200);

        // One live connection per (company, user, provider). After Revoked the user can link again.
        builder.HasIndex(x => new { x.CompanyId, x.UserId, x.Provider })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0 AND \"status\" <> 2")
            .HasDatabaseName("UX_CalendarExternalConnections_Company_User_Provider_Live");

        // Resolve the connection from the push channel id when a webhook arrives.
        builder.HasIndex(x => x.WatchChannelId)
            .IsUnique()
            .HasFilter("\"watchchannelid\" IS NOT NULL AND \"gcrecord\" = 0")
            .HasDatabaseName("UX_CalendarExternalConnections_WatchChannel_Active");

        // Worker / status panel lookup by company and recent connections.
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.ConnectedUtc })
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("IX_CalendarExternalConnections_Company_Status_ConnectedUtc");

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SourceCalendar)
            .WithMany()
            .HasForeignKey(x => x.SourceCalendarId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}