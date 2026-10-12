using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars;

/// <summary>
/// Configures the non-working periods of a user (<c>Calendar.UserNonWorkingPeriods</c>, SPEC 44).
/// </summary>
public sealed class CalendarUserNonWorkingPeriodConfiguration : IEntityTypeConfiguration<CalendarUserNonWorkingPeriod>
{
    public void Configure(EntityTypeBuilder<CalendarUserNonWorkingPeriod> builder)
    {
        builder.ToTable("UserNonWorkingPeriods", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Notes).HasMaxLength(500);

        builder.HasIndex(x => new { x.CompanyId, x.UserId, x.StartDate, x.EndDate })
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("IX_CalendarUserNonWorkingPeriods_Company_User_Range");

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
