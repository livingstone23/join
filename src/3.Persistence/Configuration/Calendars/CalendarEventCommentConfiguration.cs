// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Calendars;
using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Calendars;

/// <summary>
/// Configures <c>Calendar.EventComments</c> (SPEC 69). Comments live on the master of a series and on
/// modified occurrences; one row per author / moment. The <c>(CalendarEventId, CreatedUtc)</c> index serves the
/// "most recent first" list (scope rule B.6).
/// </summary>
public class CalendarEventCommentConfiguration : IEntityTypeConfiguration<CalendarEventComment>
{
    public void Configure(EntityTypeBuilder<CalendarEventComment> builder)
    {
        builder.ToTable("EventComments", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Body)
            .IsRequired()
            .HasMaxLength(CalendarEventComment.MaxBodyLength);

        builder.Property(x => x.DeleteReason)
            .HasMaxLength(CalendarEventComment.MaxDeleteReasonLength);

        builder.Property(x => x.CreatedUtc).IsRequired();

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CalendarEvent)
            .WithMany()
            .HasForeignKey(x => x.CalendarEventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AuthorUser)
            .WithMany()
            .HasForeignKey(x => x.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.DeletedByUser)
            .WithMany()
            .HasForeignKey(x => x.DeletedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CalendarEventId, x.CreatedUtc })
            .HasDatabaseName("IX_EventComments_Event_CreatedUtc");

        builder.HasIndex(x => x.CompanyId)
            .HasDatabaseName("IX_EventComments_Company");
    }
}

/// <summary>
/// Configures <c>Calendar.EventDocuments</c> (SPEC 69). One row per uploaded file (logical delete keeps the
/// file on disk). The <c>(CalendarEventId, Created)</c> index serves the documents list (scope rule B.8).
/// </summary>
public class CalendarEventDocumentConfiguration : IEntityTypeConfiguration<CalendarEventDocument>
{
    public void Configure(EntityTypeBuilder<CalendarEventDocument> builder)
    {
        builder.ToTable("EventDocuments", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DocumentType).IsRequired();
        builder.Property(x => x.OriginalName)
            .IsRequired()
            .HasMaxLength(CalendarEventDocument.MaxNameLength);
        builder.Property(x => x.NewName)
            .IsRequired()
            .HasMaxLength(CalendarEventDocument.MaxNameLength);
        builder.Property(x => x.Path)
            .IsRequired()
            .HasMaxLength(CalendarEventDocument.MaxPathLength);
        builder.Property(x => x.StorageProvider).IsRequired();
        builder.Property(x => x.ContentType)
            .IsRequired()
            .HasMaxLength(150);
        builder.Property(x => x.SizeBytes).IsRequired();

        builder.Property(x => x.DeleteReason)
            .HasMaxLength(CalendarEventComment.MaxDeleteReasonLength);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CalendarEvent)
            .WithMany()
            .HasForeignKey(x => x.CalendarEventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CalendarEventComment)
            .WithMany(x => x.Documents)
            .HasForeignKey(x => x.CalendarEventCommentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.UploadedByUser)
            .WithMany()
            .HasForeignKey(x => x.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.DeletedByUser)
            .WithMany()
            .HasForeignKey(x => x.DeletedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CalendarEventId, x.Created })
            .HasDatabaseName("IX_EventDocuments_Event_Created");

        builder.HasIndex(x => x.CompanyId)
            .HasDatabaseName("IX_EventDocuments_Company");

        builder.HasIndex(x => x.CalendarEventCommentId)
            .HasDatabaseName("IX_EventDocuments_Comment");
    }
}

/// <summary>
/// Configures <c>Calendar.AttachmentSettings</c> (SPEC 69), one live row per company. Without a row nothing
/// can be attached.
/// </summary>
public class CalendarAttachmentSettingsConfiguration : IEntityTypeConfiguration<CalendarAttachmentSettings>
{
    public void Configure(EntityTypeBuilder<CalendarAttachmentSettings> builder)
    {
        builder.ToTable("AttachmentSettings", "Calendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AllowedDocumentTypes).IsRequired();
        builder.Property(x => x.MaxFileSizeBytes).IsRequired();
        builder.Property(x => x.MaxFilesPerComment).IsRequired();

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CompanyId)
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_CalendarAttachmentSettings_Company");
    }
}