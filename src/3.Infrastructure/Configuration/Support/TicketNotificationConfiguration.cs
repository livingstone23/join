using JOIN.Domain.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Infrastructure.Configuration;



/// <summary>
/// Fluent API configuration for Omnichannel Notifications.
/// Supports generic system alerts (TicketId = null) or specific ticket thread messages.
/// </summary>
public class TicketNotificationConfiguration : IEntityTypeConfiguration<TicketNotification>
{
    public void Configure(EntityTypeBuilder<TicketNotification> builder)
    {
        builder.ToTable("TicketNotifications");
        builder.HasKey(tn => tn.Id);

        builder.Property(tn => tn.MessageSummary).IsRequired().HasMaxLength(500);
        builder.Property(tn => tn.ExternalProviderId).HasMaxLength(100);
        builder.Property(tn => tn.NotificationType).HasMaxLength(50);

        // Relación Opcional: Soporte para notificaciones genéricas
        builder.HasOne(tn => tn.Ticket)
            .WithMany()
            .HasForeignKey(tn => tn.TicketId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Relación Obligatoria: Canal de comunicación
        builder.HasOne(tn => tn.Channel)
            .WithMany()
            .HasForeignKey(tn => tn.CommunicationChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(tn => tn.GcRecord == 0);
    }
}