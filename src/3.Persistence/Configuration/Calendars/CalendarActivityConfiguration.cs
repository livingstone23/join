using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars;

/// <summary>
/// Configures the global activity type catalog (<c>Calendar.Activities</c>, SPEC 44).
/// </summary>
public sealed class CalendarActivityConfiguration : IEntityTypeConfiguration<CalendarActivity>
{
    public void Configure(EntityTypeBuilder<CalendarActivity> builder)
    {
        builder.ToTable("Activities", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).IsRequired().HasMaxLength(30);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Color).IsRequired().HasMaxLength(7);
        builder.Property(x => x.Icon).HasMaxLength(100);

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_CalendarActivities_Code_Active");
    }
}
