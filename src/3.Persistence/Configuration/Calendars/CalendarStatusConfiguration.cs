using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars;

/// <summary>
/// Configures the global status catalog (<c>Calendar.Statuses</c>, SPEC 44).
/// </summary>
public sealed class CalendarStatusConfiguration : IEntityTypeConfiguration<CalendarStatus>
{
    public void Configure(EntityTypeBuilder<CalendarStatus> builder)
    {
        builder.ToTable("Statuses", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).IsRequired().HasMaxLength(30);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Color).IsRequired().HasMaxLength(7);

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_CalendarStatuses_Code_Active");
    }
}
