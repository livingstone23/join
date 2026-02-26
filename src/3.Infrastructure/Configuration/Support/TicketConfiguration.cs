using JOIN.Domain.Admin;
using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Domain.Support;



/// <summary>
/// Fluent API configuration for the Support Ticket entity.
/// Defines the core SLAs, constraints, and optional relationships for the omnichannel module.
/// </summary>
public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(150);
        builder.Property(t => t.Description).HasMaxLength(2000); 
        
        // Ticket Code (e.g., 2026_001) must be unique system-wide
        builder.Property(t => t.Code).IsRequired().HasMaxLength(50);
        builder.HasIndex(t => t.Code).IsUnique();

        // Relación Obligatoria: Multi-Tenant boundary
        builder.HasOne(t => t.Company)
            .WithMany()
            .HasForeignKey(t => t.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Relaciones Opcionales (Project y Area)
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

        builder.HasQueryFilter(t => t.GcRecord == 0);
    }
}
