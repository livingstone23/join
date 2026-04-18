// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Messaging;



/// <summary>
/// Configures the database mapping for the <see cref="TicketStatus"/> entity.
/// This setup defines the table structure, constraints, and relationships
/// for the ticket status data in the persistence layer.
/// </summary>
public class TicketStatusConfiguration : IEntityTypeConfiguration<TicketStatus>
{
    /// <summary>
    /// Configures the <see cref="TicketStatus"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<TicketStatus> builder)
    {
        // Maps the entity to the "TicketStatuses" table in the "Messaging" schema.
        builder.ToTable("TicketStatuses", "Messaging");

        // Sets the primary key for the entity.
        builder.HasKey(p => p.Id);

        // Configures the CompanyId property as a required tenant discriminator.
        builder.Property(p => p.CompanyId)
            .IsRequired();

        // Configures the 'Name' property: it is required and has a maximum length of 50 characters.
        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(50);

        // Configures the 'Code' property as a required numeric identifier.
        builder.Property(p => p.Code)
            .IsRequired();

        // Configures the 'Description' property: it is optional and has a maximum length of 200 characters.
        builder.Property(p => p.Description)
            .IsRequired(false)
            .HasMaxLength(200);

        // Configures the 'IsActive' property as required.
        builder.Property(p => p.IsActive)
            .IsRequired();

        // Configures the workflow control flags as required.
        builder.Property(p => p.IsInitial)
            .IsRequired();

        builder.Property(p => p.IsPaused)
            .IsRequired();

        builder.Property(p => p.IsFinal)
            .IsRequired();

        // Filtered unique indexes to guarantee only one logical initial/paused/final status per company.
        builder.HasIndex(p => new { p.CompanyId, p.IsInitial })
            .HasDatabaseName("UX_TicketStatuses_Company_Initial")
            .IsUnique()
            .HasFilter("[IsInitial] = 1 AND [GcRecord] = 0");

        builder.HasIndex(p => new { p.CompanyId, p.IsPaused })
            .HasDatabaseName("UX_TicketStatuses_Company_Paused")
            .IsUnique()
            .HasFilter("[IsPaused] = 1 AND [GcRecord] = 0");

        builder.HasIndex(p => new { p.CompanyId, p.IsFinal })
            .HasDatabaseName("UX_TicketStatuses_Company_Final")
            .IsUnique()
            .HasFilter("[IsFinal] = 1 AND [GcRecord] = 0");

        // Apply a soft-delete filter to automatically exclude records marked as deleted.
        builder.HasQueryFilter(a => a.GcRecord == 0);

        
    }
}
