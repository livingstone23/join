// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Messaging;



/// <summary>
/// Configures the database mapping for the <see cref="TicketComplexity"/> entity.
/// This setup defines the table structure and constraints for the global ticket complexity catalog.
/// </summary>
public class TicketComplexityConfiguration : IEntityTypeConfiguration<TicketComplexity>
{
    /// <summary>
    /// Configures the <see cref="TicketComplexity"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<TicketComplexity> builder)
    {
        builder.ToTable("TicketComplexities", "Messaging");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.CompanyId)
            .IsRequired();

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.Description)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(p => p.Code)
            .IsRequired();

        builder.Property(p => p.ResolutionTimeUnits)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .IsRequired();

        builder.HasOne(p => p.Company)
            .WithMany()
            .HasForeignKey(p => p.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.TimeUnit)
            .WithMany(t => t.TicketComplexities)
            .HasForeignKey(p => p.TimeUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.CompanyId, p.Name })
            .HasDatabaseName("UX_TicketComplexities_Company_Name")
            .IsUnique()
            .HasFilter("[GcRecord] = 0");

        builder.HasQueryFilter(a => a.GcRecord == 0);
    }
}
