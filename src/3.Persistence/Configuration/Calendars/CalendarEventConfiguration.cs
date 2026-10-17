using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars;

/// <summary>
/// Configures the calendar activities (<c>Calendar.Events</c>, SPEC 44). Every relationship is
/// <see cref="DeleteBehavior.Restrict"/>: the module only deletes logically.
/// </summary>
public sealed class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.ToTable("Events", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Summary).HasMaxLength(500);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Location).HasMaxLength(300);
        builder.Property(x => x.VideoCallUrl).HasMaxLength(500);
        builder.Property(x => x.RecurrenceRule).HasMaxLength(500);
        builder.Property(x => x.OriginIdentifier).HasMaxLength(200);
        builder.Property(x => x.OriginReference).HasMaxLength(200);

        // One exception per occurrence of a series.
        builder.HasIndex(x => new { x.SeriesMasterId, x.OriginalStartUtc })
            .IsUnique()
            .HasFilter("\"seriesmasterid\" IS NOT NULL AND \"gcrecord\" = 0")
            .HasDatabaseName("UX_CalendarEvents_SeriesMaster_OriginalStart_Active");

        // Deduplicates agent retries (SPEC 45).
        builder.HasIndex(x => new { x.CompanyId, x.OriginChannelId, x.OriginReference })
            .IsUnique()
            .HasFilter("\"originreference\" IS NOT NULL AND \"gcrecord\" = 0")
            .HasDatabaseName("UX_CalendarEvents_Company_Channel_OriginReference_Active");

        builder.HasIndex(x => new { x.CompanyId, x.OwnerUserId, x.StartUtc, x.EndUtc })
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("IX_CalendarEvents_Owner_Range");

        builder.HasIndex(x => new { x.CalendarId, x.StartUtc })
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("IX_CalendarEvents_Calendar_Range");

        builder.HasIndex(x => new { x.CompanyId, x.OwnerUserId, x.RecurrenceEndUtc })
            .HasFilter("\"recurrencerule\" IS NOT NULL AND \"gcrecord\" = 0")
            .HasDatabaseName("IX_CalendarEvents_Owner_Recurring");

        builder.HasIndex(x => x.RequesterPersonId)
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("IX_CalendarEvents_RequesterPerson");

        builder.HasIndex(x => new { x.CompanyId, x.LeadId })
            .HasFilter("\"leadid\" IS NOT NULL AND \"gcrecord\" = 0")
            .HasDatabaseName("IX_CalendarEvents_Company_Lead");
        builder.HasOne<JOIN.Domain.Customers.Leads.Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.TicketId)
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("IX_CalendarEvents_Ticket");

        builder.HasOne(x => x.Calendar)
            .WithMany(x => x.Events)
            .HasForeignKey(x => x.CalendarId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.OwnerUser)
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CalendarActivityCompany)
            .WithMany()
            .HasForeignKey(x => x.CalendarActivityCompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CalendarStatusCompany)
            .WithMany()
            .HasForeignKey(x => x.CalendarStatusCompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SeriesMaster)
            .WithMany(x => x.Exceptions)
            .HasForeignKey(x => x.SeriesMasterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.OriginChannel)
            .WithMany()
            .HasForeignKey(x => x.OriginChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.OriginUser)
            .WithMany()
            .HasForeignKey(x => x.OriginUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RequesterPerson)
            .WithMany()
            .HasForeignKey(x => x.RequesterPersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Ticket)
            .WithMany()
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StatusChangedByUser)
            .WithMany()
            .HasForeignKey(x => x.StatusChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
