using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Admin;

/// <summary>
/// Configures <c>Admin.PersonPendingConfirmations</c> (SPEC 45): people arriving through the channel agent
/// that are awaiting review. All relationships are <see cref="DeleteBehavior.Restrict"/>; the module only
/// deletes logically.
/// </summary>
public sealed class PersonPendingConfirmationConfiguration : IEntityTypeConfiguration<PersonPendingConfirmation>
{
    public void Configure(EntityTypeBuilder<PersonPendingConfirmation> builder)
    {
        builder.ToTable("PersonPendingConfirmations", "Admin");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).IsRequired().HasMaxLength(500);
        builder.Property(x => x.ChannelIdentifier).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ResolutionNotes).HasMaxLength(1000);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>();

        // A person can have at most ONE open row at a time.
        builder.HasIndex(x => new { x.CompanyId, x.PersonId })
            .IsUnique()
            .HasFilter("\"status\" = 1 AND \"gcrecord\" = 0")
            .HasDatabaseName("UX_PersonPendingConfirmations_Company_Person_Pending");

        // Inbox lookup (paged, most recent first).
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.RequestedUtc })
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("IX_PersonPendingConfirmations_Company_Status_RequestedUtc");

        builder.HasOne(x => x.Person)
            .WithMany()
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CommunicationChannel)
            .WithMany()
            .HasForeignKey(x => x.CommunicationChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReviewEvent)
            .WithMany()
            .HasForeignKey(x => x.ReviewEventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ResolvedByUser)
            .WithMany()
            .HasForeignKey(x => x.ResolvedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}