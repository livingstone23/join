using JOIN.Domain.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Infrastructure.Configuration;



/// <summary>
/// Fluent API configuration for Omnichannel Notifications.
/// Secures the tracking of outbound messages, supporting both ticket-specific and generic alerts.
/// </summary>
public class TicketNotificationConfiguration : IEntityTypeConfiguration<TicketNotification>
{
    /// <summary>
    /// Applies the configuration rules using the provided builder.
    /// </summary>
    public void Configure(EntityTypeBuilder<TicketNotification> builder)
    {
        // 1. Table & Schema Mapping
        // Isolated in the "Support" schema to separate omnichannel traffic from core admin data.
        builder.ToTable("TicketNotifications", "Support");

        // 2. Primary Key
        builder.HasKey(tn => tn.Id);

        // 3. Property Constraints
        builder.Property(tn => tn.MessageSummary)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(tn => tn.ExternalProviderId)
            .HasMaxLength(100);

        builder.Property(tn => tn.NotificationType)
            .IsRequired()
            .HasMaxLength(50);

        // 4. Performance Indexes (CRITICAL)
        // Heavily optimizes queries like: "Get all notification history for Ticket X"
        builder.HasIndex(tn => tn.TicketId);

        // Optimizes analytics queries like: "How many WhatsApp messages did we send today?"
        builder.HasIndex(tn => tn.CommunicationChannelId);

        // 5. Relationships (Foreign Keys & Delete Behaviors)

        // Optional Relationship: Support for generic system alerts (TicketId = null)
        builder.HasOne(tn => tn.Ticket)
            // Use t => t.TicketNotifications ONLY if you add the collection to the Ticket class
            .WithMany() 
            .HasForeignKey(tn => tn.TicketId)
            .IsRequired(false) // Explicitly mark as nullable relationship
            .OnDelete(DeleteBehavior.Restrict); // Do not delete a ticket if it has an audit trail of notifications.

        // Required Relationship: Communication Channel (WhatsApp, SendGrid, SMS)
        builder.HasOne(tn => tn.Channel)
            .WithMany()
            .HasForeignKey(tn => tn.CommunicationChannelId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent deleting a channel if it has history.

        // 6. Global Query Filters
        // Automatically excludes soft-deleted notifications from standard queries.
        builder.HasQueryFilter(tn => tn.GcRecord == 0);
    }
}