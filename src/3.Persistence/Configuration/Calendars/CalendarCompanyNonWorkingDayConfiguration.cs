using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars;

/// <summary>
/// Configures the company holidays (<c>Calendar.CompanyNonWorkingDays</c>, SPEC 44).
/// </summary>
public sealed class CalendarCompanyNonWorkingDayConfiguration : IEntityTypeConfiguration<CalendarCompanyNonWorkingDay>
{
    public void Configure(EntityTypeBuilder<CalendarCompanyNonWorkingDay> builder)
    {
        builder.ToTable("CompanyNonWorkingDays", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);

        builder.HasIndex(x => new { x.CompanyId, x.Date })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_CalendarCompanyNonWorkingDays_Company_Date_Active");
    }
}
