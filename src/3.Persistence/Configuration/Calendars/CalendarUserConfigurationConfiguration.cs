using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars;

/// <summary>
/// Configures the calendar configuration of a user in a company (<c>Calendar.UserConfigurations</c>, SPEC 44).
/// The unique index on <c>(CompanyId, UserId)</c> is what guarantees a single default calendar per user and company.
/// </summary>
public sealed class CalendarUserConfigurationConfiguration : IEntityTypeConfiguration<CalendarUserConfiguration>
{
    public void Configure(EntityTypeBuilder<CalendarUserConfiguration> builder)
    {
        builder.ToTable("UserConfigurations", "Calendar");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.CompanyId, x.UserId })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_CalendarUserConfigurations_Company_User_Active");

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.DefaultCalendar)
            .WithMany()
            .HasForeignKey(x => x.DefaultCalendarId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
