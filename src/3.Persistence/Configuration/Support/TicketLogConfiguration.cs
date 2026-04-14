using JOIN.Domain.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configurations;



/// <summary>
/// Fluent API configuration for Ticket Audit Logs.
/// Ensures high-performance tracking of status changes, reassignments, and work logs 
/// while maintaining strict referential integrity for multi-tenant environments.
/// </summary>
public class TicketLogConfiguration : IEntityTypeConfiguration<TicketLog>
{

    public void Configure(EntityTypeBuilder<TicketLog> builder)
    {
        // --- Table & Primary Key ---
        builder.ToTable("TicketLogs", "Support");
        builder.HasKey(tl => tl.Id);

        // --- Properties ---
        builder.Property(tl => tl.Summary)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(tl => tl.LogType)
            .IsRequired();

        builder.Property(tl => tl.ConsumedTime)
            .HasPrecision(18, 2);

        // --- Indexes for High Performance (Pilar 2) ---
        builder.HasIndex(tl => tl.TicketId);
        builder.HasIndex(tl => tl.UserRegisterLogId);
        builder.HasIndex(tl => new { tl.CompanyId, tl.TicketId });

        // --- Relationships ---

        // Principal Relationship: Associated Ticket
        builder.HasOne(tl => tl.Ticket)
            .WithMany(t => t.TicketLogs)
            .HasForeignKey(tl => tl.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // Audit Relationship: User who performed the action
        builder.HasOne(tl => tl.UserRegistered)
            .WithMany()
            .HasForeignKey(tl => tl.UserRegisterLogId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reassignment Relationship: New user responsible (Optional)
        builder.HasOne(tl => tl.NewAssignedToUser)
            .WithMany()
            .HasForeignKey(tl => tl.NewAssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Status Tracking: Current status at the time of log
        builder.HasOne(tl => tl.Status)
            .WithMany()
            .HasForeignKey(tl => tl.TicketStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        // Status Tracking: Previous status for SLA and history (Optional)
        builder.HasOne(tl => tl.PreviousStatus)
            .WithMany()
            .HasForeignKey(tl => tl.PreviousStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        // Work Log: Time unit measurement (Optional)
        builder.HasOne(tl => tl.TimeUnit)
            .WithMany()
            .HasForeignKey(tl => tl.TimeUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Global Filters ---
        // Soft delete filter (matches your pattern)
        builder.HasQueryFilter(tl => tl.GcRecord == 0);

    }

}