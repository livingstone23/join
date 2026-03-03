


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

    public void Configure(EntityTypeBuilder<TicketNotification> builder)
    {

        builder.ToTable("TicketNotifications", "Support");
        builder.HasKey(tn => tn.Id);

        builder.Property(tn => tn.MessageSummary).IsRequired().HasMaxLength(500);
        builder.Property(tn => tn.ExternalProviderId).HasMaxLength(100);
        builder.Property(tn => tn.NotificationType).IsRequired().HasMaxLength(50);

        builder.HasIndex(tn => tn.TicketId);
        builder.HasIndex(tn => tn.CommunicationChannelId);

        // --- Relationships ---

        // Optional Relationship: Support for generic system alerts (TicketId = null)
        builder.HasOne(tn => tn.Ticket)
            .WithMany(t => t.Notifications)  // CORREGIDO: Evita shadow property TicketId1
            .HasForeignKey(tn => tn.TicketId)
            .IsRequired(false) 
            .OnDelete(DeleteBehavior.Restrict);

        // Required Relationship: Communication Channel
        builder.HasOne(tn => tn.Channel)
            .WithMany() // Se asume que Channel no tiene una lista de Notificaciones. Si la tiene, pon (c => c.Notifications)
            .HasForeignKey(tn => tn.CommunicationChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(tn => tn.GcRecord == 0);

    }

}