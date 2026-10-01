using JOIN.Domain.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Support;

/// <summary>
/// Fluent API configuration for ticket attachment documents.
///
/// The two FKs use <c>Restrict</c> (not cascade) so a deletion of a
/// <c>Ticket</c> or <c>TicketLog</c> does not silently destroy its evidence.
/// The two secondary indexes support the two hot reads: per-ticket listing
/// (<c>(TicketId, Created)</c>) and per-tenant daily quota counting
/// (<c>(CompanyId, Created)</c>).
/// </summary>
public class TicketDocumentConfiguration : IEntityTypeConfiguration<TicketDocument>
{
    /// <summary>
    /// Configures the entity schema, constraints, indexes, and relationships.
    /// </summary>
    public void Configure(EntityTypeBuilder<TicketDocument> builder)
    {
        // --- Table & Primary Key ---
        builder.ToTable("TicketDocuments", "Support");
        builder.HasKey(td => td.Id);

        // --- Properties ---
        builder.Property(td => td.TicketId).IsRequired();
        builder.Property(td => td.TicketLogsId).IsRequired();
        builder.Property(td => td.DocumentType).IsRequired();

        builder.Property(td => td.OriginalName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(td => td.NewName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(td => td.Path)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(td => td.StorageProvider)
            .IsRequired();

        builder.Property(td => td.ContentType)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(td => td.SizeBytes)
            .IsRequired();

        // --- Indexes ---
        // Per-ticket listing (GetTicketDocuments) orders by Created DESC.
        builder.HasIndex(td => new { td.TicketId, td.Created })
            .HasDatabaseName("IX_TicketDocuments_Ticket_Created");

        // Per-tenant daily quota count: filter by CompanyId + Created range.
        builder.HasIndex(td => new { td.CompanyId, td.Created })
            .HasDatabaseName("IX_TicketDocuments_Company_Created");

        // --- Relationships ---
        builder.HasOne(td => td.Ticket)
            .WithMany()
            .HasForeignKey(td => td.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(td => td.TicketLog)
            .WithMany()
            .HasForeignKey(td => td.TicketLogsId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Global Filters ---
        builder.HasQueryFilter(td => td.GcRecord == 0);
    }
}