using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars;

/// <summary>
/// Configures the activity log (<c>Calendar.EventLogs</c>, SPEC 44).
/// </summary>
public sealed class CalendarEventLogConfiguration : IEntityTypeConfiguration<CalendarEventLog>
{
    public void Configure(EntityTypeBuilder<CalendarEventLog> builder)
    {
        builder.ToTable("EventLogs", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActorIdentifier).HasMaxLength(200);
        builder.Property(x => x.Notes).HasMaxLength(1000);

        builder.HasIndex(x => new { x.CompanyId, x.CalendarEventId, x.OccurredUtc })
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("IX_CalendarEventLogs_Company_Event_Occurred");

        builder.HasOne(x => x.CalendarEvent)
            .WithMany(x => x.Logs)
            .HasForeignKey(x => x.CalendarEventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Channel)
            .WithMany()
            .HasForeignKey(x => x.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ActorUser)
            .WithMany()
            .HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
