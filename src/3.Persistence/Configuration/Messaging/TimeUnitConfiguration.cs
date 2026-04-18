// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Messaging;



/// <summary>
/// Configures the database mapping for the <see cref="TimeUnit"/> entity.
/// This setup defines the table for time measurements (e.g., Hours, Days)
/// used in SLA calculations and time tracking.
/// </summary>
public class TimeUnitConfiguration : IEntityTypeConfiguration<TimeUnit>
{
    /// <summary>
    /// Configures the <see cref="TimeUnit"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<TimeUnit> builder)
    {
                
        // Maps the entity to the "TimeUnits" table in the "Messaging" schema.
        builder.ToTable("TimeUnits", "Messaging");

        // Sets the primary key for the entity.
        builder.HasKey(p => p.Id);

        // Configures the CompanyId property as a required tenant discriminator.
        builder.Property(p => p.CompanyId)
            .IsRequired();

        // Configures the 'Name' property: it is required and has a maximum length of 50 characters.
        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(50);

        // Configures the 'Code' property: it is required.
        builder.Property(p => p.Code)
            .IsRequired();
        
        // --- Relationships ---

        builder.HasOne(p => p.Company)
            .WithMany()
            .HasForeignKey(p => p.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configures the one-to-many relationship with Ticket.
        // A TimeUnit can be associated with many Tickets.
        builder.HasMany(p => p.Tickets)
            .WithOne(t => t.TimeUnit)
            .HasForeignKey(t => t.TimeUnitId);

        builder.HasIndex(p => new { p.CompanyId, p.Name })
            .HasDatabaseName("UX_TimeUnits_Company_Name")
            .IsUnique()
            .HasFilter("[GcRecord] = 0");

        // Apply a soft-delete filter to automatically exclude records marked as deleted.
        builder.HasQueryFilter(a => a.GcRecord == 0);


    }
}
