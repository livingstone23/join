// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Infrastructure.Configuration.Messaging;



/// <summary>
/// Configures the database mapping for the <see cref="Ticket"/> entity.
/// This is a central entity, defining its properties, relationships, and constraints
/// for service requests, SLAs, and multi-channel communication.
/// </summary>
public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    /// <summary>
    /// Configures the <see cref="Ticket"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        // Sets the primary key for the entity.
        builder.HasKey(t => t.Id);

        // --- Properties ---

        // Configures the 'Code' property: it is required, has a max length, and must be unique.
        builder.Property(t => t.Code)
            .IsRequired()
            .HasMaxLength(50);
        builder.HasIndex(t => t.Code).IsUnique();

        // Configures 'Name' and 'Description' properties with length constraints.
        builder.Property(t => t.Name).IsRequired().HasMaxLength(150);
        builder.Property(t => t.Description).HasMaxLength(2000);

        // Configures time-related properties with precision.
        builder.Property(t => t.EstimatedTime).HasPrecision(18, 2);
        builder.Property(t => t.ConsumedTime).HasPrecision(18, 2);
        
        // --- Relationships ---

        // Required relationship with Company (multi-tenant boundary).
        builder.HasOne(t => t.Company)
            .WithMany()
            .HasForeignKey(t => t.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional relationship with Customer.
        builder.HasOne(t => t.Customer)
            .WithMany()
            .HasForeignKey(t => t.CustomerId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // Required relationship with the user who created the ticket.
        builder.HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional relationship with the user assigned to the ticket.
        builder.HasOne(t => t.AssignedToUser)
            .WithMany()
            .HasForeignKey(t => t.AssignedToUserId)
            .IsRequired(false) // A ticket might not have an assigned user.
            .OnDelete(DeleteBehavior.Restrict);

        // Relationships with catalogs: Status, Complexity, and TimeUnit.
        builder.HasOne(t => t.Status)
            .WithMany(s => s.Tickets)
            .HasForeignKey(t => t.TicketStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Complexity)
            .WithMany(c => c.Tickets)
            .HasForeignKey(t => t.TicketComplexityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.TimeUnit)
            .WithMany(tu => tu.Tickets)
            .HasForeignKey(t => t.TimeUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        
        // Relationship with the channel of creation.
        builder.HasOne(t => t.Channel)
            .WithMany()
            .HasForeignKey(t => t.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional self-referencing relationship for parent/child tickets.
        builder.HasOne(t => t.PrecedentTicket)
            .WithMany()
            .HasForeignKey(t => t.PrecedentTicketId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // Optional relationships with Project and Area.
        builder.HasOne(t => t.Project)
            .WithMany()
            .HasForeignKey(t => t.ProjectId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.Area)
            .WithMany()
            .HasForeignKey(t => t.AreaId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // --- Table Mapping ---

        // Maps the entity to the "Tickets" table in the "Messaging" schema.
        builder.ToTable("Tickets", "Messaging");
    }
}
