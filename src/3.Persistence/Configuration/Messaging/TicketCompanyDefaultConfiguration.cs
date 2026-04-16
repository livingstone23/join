
using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Messaging;

/// <summary>
/// Configures persistence rules for tenant ticket default settings.
/// </summary>
public sealed class TicketCompanyDefaultConfiguration : IEntityTypeConfiguration<TicketCompanyDefault>
{
    /// <summary>
    /// Configures the entity schema, constraints, and relationships.
    /// </summary>
    public void Configure(EntityTypeBuilder<TicketCompanyDefault> builder)
    {
        builder.ToTable("TicketCompanyDefaults", "Messaging");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StartCode)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.CodeSequenceLength)
            .IsRequired();

        builder.Property(x => x.UsePersonalizedCode)
            .IsRequired();

        builder.HasIndex(x => x.CompanyId)
            .IsUnique();

        builder.HasOne(x => x.TicketStatusDefault)
            .WithMany()
            .HasForeignKey(x => x.TicketStatusDefaultId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.TicketComplexityDefault)
            .WithMany()
            .HasForeignKey(x => x.TicketComplexityDefaultId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.TimeUnitDefault)
            .WithMany()
            .HasForeignKey(x => x.TimeUnitDefaultId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AreaDefault)
            .WithMany()
            .HasForeignKey(x => x.AreaDefaultId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProjectDefault)
            .WithMany()
            .HasForeignKey(x => x.ProjectDefaultId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ChannelDefault)
            .WithMany()
            .HasForeignKey(x => x.ChannelDefaultId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
