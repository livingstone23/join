using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars;

/// <summary>
/// Configures the activity types enabled per company (<c>Calendar.ActivityCompanies</c>, SPEC 44).
/// </summary>
public sealed class CalendarActivityCompanyConfiguration : IEntityTypeConfiguration<CalendarActivityCompany>
{
    public void Configure(EntityTypeBuilder<CalendarActivityCompany> builder)
    {
        builder.ToTable("ActivityCompanies", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Color).IsRequired().HasMaxLength(7);

        builder.HasIndex(x => new { x.CompanyId, x.CalendarActivityId })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_CalendarActivityCompanies_Company_Activity_Active");

        builder.HasOne(x => x.CalendarActivity)
            .WithMany(x => x.ActivityCompanies)
            .HasForeignKey(x => x.CalendarActivityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InitialCalendarStatusCompany)
            .WithMany()
            .HasForeignKey(x => x.InitialCalendarStatusCompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
