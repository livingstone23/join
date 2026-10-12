using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars;

/// <summary>
/// Configures the calendars of each user (<c>Calendar.UserCalendars</c>, SPEC 44).
/// </summary>
public sealed class UserCalendarConfiguration : IEntityTypeConfiguration<UserCalendar>
{
    public void Configure(EntityTypeBuilder<UserCalendar> builder)
    {
        builder.ToTable("UserCalendars", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Color).IsRequired().HasMaxLength(7);

        builder.HasIndex(x => new { x.CompanyId, x.OwnerUserId, x.Name })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_UserCalendars_Company_Owner_Name_Active");

        builder.HasOne(x => x.OwnerUser)
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
