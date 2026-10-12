using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars.External;

/// <summary>
/// Configures the outbox of pending sync operations (<c>Calendar.SyncOutbox</c>, SPEC 46-post).
/// Only one <c>Pending</c> row per <c>(ConnectionId, CalendarEventId)</c>; the worker queue is
/// served by the <c>(Status, NextAttemptUtc)</c> index.
/// </summary>
public sealed class CalendarSyncOutboxItemConfiguration : IEntityTypeConfiguration<CalendarSyncOutboxItem>
{
    public void Configure(EntityTypeBuilder<CalendarSyncOutboxItem> builder)
    {
        builder.ToTable("SyncOutbox", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.LastError).HasMaxLength(1000);

        // Worker queue: process by due time.
        builder.HasIndex(x => new { x.Status, x.NextAttemptUtc })
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("IX_CalendarSyncOutbox_Status_NextAttemptUtc_Active");

        // One pending per (connection, event): a newer change updates the existing pending row.
        builder.HasIndex(x => new { x.ConnectionId, x.CalendarEventId })
            .IsUnique()
            .HasFilter("\"status\" = 1 AND \"gcrecord\" = 0")
            .HasDatabaseName("UX_CalendarSyncOutbox_Connection_Event_Pending");

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