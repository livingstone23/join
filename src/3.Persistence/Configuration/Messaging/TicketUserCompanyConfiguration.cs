using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Messaging;

/// <summary>
/// Configures the database mapping for the <see cref="TicketUserCompany"/> entity.
/// Defines the tenant-scoped link between a User and a Company for the ticket workflow,
/// along with the invariants (single active row per user-company) enforced by the database.
/// </summary>
public class TicketUserCompanyConfiguration : IEntityTypeConfiguration<TicketUserCompany>
{
    public void Configure(EntityTypeBuilder<TicketUserCompany> builder)
    {
        builder.ToTable("TicketUserCompanies", "Messaging");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.IsSuperAdminTicket).IsRequired();
        builder.Property(x => x.CanFinishTicket).IsRequired();
        builder.Property(x => x.CanResolveTicket).IsRequired();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // One active row per (User, Company). Filtered so a soft-deleted row
        // does not block re-adding the same user to the roster later.
        builder.HasIndex(x => new { x.UserId, x.CompanyId })
            .HasDatabaseName("UX_TicketUserCompanies_User_Company")
            .IsUnique()
            .HasFilter("[GcRecord] = 0");

        builder.HasQueryFilter(x => x.GcRecord == 0);
    }
}
