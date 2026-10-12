using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars.External;

/// <summary>
/// Configures the link between a JOIN activity and its external counterpart
/// (<c>Calendar.ExternalEventLinks</c>, SPEC 46-post).
/// </summary>
public sealed class CalendarExternalEventLinkConfiguration : IEntityTypeConfiguration<CalendarExternalEventLink>
{
    public void Configure(EntityTypeBuilder<CalendarExternalEventLink> builder)
    {
        builder.ToTable("ExternalEventLinks", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExternalEventId).IsRequired().HasMaxLength(1024);
        builder.Property(x => x.ExternalICalUid).HasMaxLength(1024);
        builder.Property(x => x.ExternalEtag).HasMaxLength(200);
        builder.Property(x => x.LastError).HasMaxLength(1000);

        builder.HasIndex(x => new { x.ConnectionId, x.CalendarEventId })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_CalendarExternalEventLinks_Connection_Event_Active");

        builder.HasIndex(x => new { x.ConnectionId, x.ExternalEventId })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_CalendarExternalEventLinks_Connection_ExternalEvent_Active");

        builder.HasOne(x => x.CalendarEvent)
            .WithMany()
            .HasForeignKey(x => x.CalendarEventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Connection)
            .WithMany()
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}