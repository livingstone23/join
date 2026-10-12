using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars;

/// <summary>
/// Configures the statuses enabled per company (<c>Calendar.StatusCompanies</c>, SPEC 44).
/// </summary>
public sealed class CalendarStatusCompanyConfiguration : IEntityTypeConfiguration<CalendarStatusCompany>
{
    public void Configure(EntityTypeBuilder<CalendarStatusCompany> builder)
    {
        builder.ToTable("StatusCompanies", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Color).IsRequired().HasMaxLength(7);

        builder.HasIndex(x => new { x.CompanyId, x.CalendarStatusId })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_CalendarStatusCompanies_Company_Status_Active");

        builder.HasOne(x => x.CalendarStatus)
            .WithMany(x => x.StatusCompanies)
            .HasForeignKey(x => x.CalendarStatusId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
