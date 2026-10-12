using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars;

/// <summary>
/// Configures the calendar configuration of each company (<c>Calendar.CalendarCompanies</c>, SPEC 44):
/// one active row per company.
/// </summary>
public sealed class CalendarCompanyConfiguration : IEntityTypeConfiguration<CalendarCompany>
{
    public void Configure(EntityTypeBuilder<CalendarCompany> builder)
    {
        builder.ToTable("CalendarCompanies", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TimeZone).IsRequired().HasMaxLength(64);
        builder.Property(x => x.DefaultCalendarName).IsRequired().HasMaxLength(100);

        builder.HasIndex(x => x.CompanyId)
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_CalendarCompanies_Company_Active");

        builder.HasOne(x => x.DefaultActivityUser)
            .WithMany()
            .HasForeignKey(x => x.DefaultActivityUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RescheduledByOthersStatusCompany)
            .WithMany()
            .HasForeignKey(x => x.RescheduledByOthersStatusCompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
