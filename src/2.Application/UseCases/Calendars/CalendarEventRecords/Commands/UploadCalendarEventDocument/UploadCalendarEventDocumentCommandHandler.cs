using System.IO;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;

/// <summary>
/// SPEC 69 — handles <see cref="UploadCalendarEventDocumentCommand"/>: verifies the activity is visible
/// (rule 5), loads the company's <c>CalendarAttachmentSettings</c>, infers the <see cref="DocumentType"/>
/// from the extension, enforces the size / type / per-comment / daily quotas, persists the file via
/// <see cref="IFileStorageResolver"/> under <c>calendar/{company}/{event}/...</c> (rule 9), records the row and
/// writes the <c>DocumentAdded</c> log.
/// </summary>
public sealed class UploadCalendarEventDocumentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarEventRecordsAccess access,
    IFileStorageResolver fileStorageResolver,
    CalendarEventLogWriter logWriter)
    : IRequestHandler<UploadCalendarEventDocumentCommand, Response<CalendarEventDocumentDto>>
{
    public async Task<Response<CalendarEventDocumentDto>> Handle(
        UploadCalendarEventDocumentCommand request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUserService.UserId, out var uploaderId) || uploaderId == Guid.Empty)
        {
            return Response<CalendarEventDocumentDto>.Error(CalendarErrorCodes.UserRequired,
                ["The authenticated user could not be resolved."]);
        }

        var eventAccess = await access.ResolveAsync(request.CalendarEventId, cancellationToken);
        if (!eventAccess.IsFoundAndVisible)
        {
            return Response<CalendarEventDocumentDto>.Error(CalendarErrorCodes.EventNotFound,
                ["The calendar activity was not found or is not visible."]);
        }

        var calendarEvent = eventAccess.CalendarEvent!;

        var settings = (await unitOfWork.GetRepository<Domain.Calendars.CalendarAttachmentSettings>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == calendarEvent.CompanyId && x.GcRecord == 0))
            .FirstOrDefault();
        if (settings is null)
        {
            return Response<CalendarEventDocumentDto>.Error(CalendarErrorCodes.AttachmentSettingsNotConfigured,
                ["The company has no calendar attachment settings row yet."]);
        }

        var extension = Path.GetExtension(request.OriginalName)?.ToLowerInvariant() ?? string.Empty;
        if (!TryMapExtension(extension, out var documentType))
        {
            return Response<CalendarEventDocumentDto>.Error(CalendarErrorCodes.DocumentTypeNotAllowed,
                [$"The extension '{extension}' is not supported."]);
        }

        if (!settings.AllowedDocumentTypes.HasFlag(documentType))
        {
            return Response<CalendarEventDocumentDto>.Error(CalendarErrorCodes.DocumentTypeNotAllowed,
                [$"The document type '{documentType}' is not allowed for the current tenant."]);
        }

        var sizeBytes = request.Content.LongLength;
        if (sizeBytes > settings.MaxFileSizeBytes)
        {
            return Response<CalendarEventDocumentDto>.Error(CalendarErrorCodes.DocumentTooLarge,
                [$"File size {sizeBytes} bytes exceeds the maximum allowed ({settings.MaxFileSizeBytes} bytes)."]);
        }

        // Rule 7 — per-comment limit, when the document is attached to a comment. Documents an administrator
        // deleted no longer take a slot.
        if (request.CalendarEventCommentId is { } commentId)
        {
            var comment = (await unitOfWork.GetRepository<Domain.Calendars.CalendarEventComment>()
                .GetAllIncludingDeletedAsync(x => x.Id == commentId))
                .FirstOrDefault();
            if (comment is null
                || comment.GcRecord != 0
                || comment.CalendarEventId != calendarEvent.Id
                || comment.DeletedUtc is not null)
            {
                return Response<CalendarEventDocumentDto>.Error(CalendarErrorCodes.CommentNotFound,
                    ["The supplied comment does not belong to the current activity."]);
            }

            var liveForComment = (await unitOfWork.GetRepository<Domain.Calendars.CalendarEventDocument>()
                .GetAllIncludingDeletedAsync(x => x.CalendarEventCommentId == commentId
                    && x.GcRecord == 0
                    && x.DeletedUtc == null))
                .Count();
            if (liveForComment >= settings.MaxFilesPerComment)
            {
                return Response<CalendarEventDocumentDto>.Error(CalendarErrorCodes.DocumentLimitPerComment,
                    [$"The comment already has {liveForComment} attachments (max {settings.MaxFilesPerComment})."]);
            }
        }

        // Rule 7 — daily quota.
        if (settings.MaxFilesPerDay is { } perDay)
        {
            var todayUtc = DateTime.UtcNow.Date;
            var todayCount = (await unitOfWork.GetRepository<Domain.Calendars.CalendarEventDocument>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == calendarEvent.CompanyId
                    && x.GcRecord == 0
                    && x.Created >= todayUtc))
                .Count();
            if (todayCount >= perDay)
            {
                return Response<CalendarEventDocumentDto>.Error(CalendarErrorCodes.DocumentLimitPerDay,
                    [$"The company has reached the daily quota of {perDay} attachments."]);
            }
        }

        var webChannel = (await unitOfWork.GetRepository<CommunicationChannel>()
            .GetAllIncludingDeletedAsync(x => x.Code == CommunicationChannelCodes.Web && x.GcRecord == 0))
            .FirstOrDefault();
        if (webChannel is null)
        {
            return Response<CalendarEventDocumentDto>.Error(CalendarErrorCodes.WebChannelNotFound,
                ["The WEB communication channel is missing (SPEC 00)."]);
        }

        var newName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
        var storageKey = $"calendar/{calendarEvent.CompanyId}/{calendarEvent.Id}/{newName}";
        var activeProvider = fileStorageResolver.Current;
        await using var content = new MemoryStream(request.Content);
        var saveResult = await activeProvider.SaveAsync(content, storageKey, request.ContentType, cancellationToken);

        var document = new Domain.Calendars.CalendarEventDocument
        {
            CompanyId = calendarEvent.CompanyId,
            CalendarEventId = calendarEvent.Id,
            CalendarEventCommentId = request.CalendarEventCommentId,
            DocumentType = documentType,
            OriginalName = request.OriginalName,
            NewName = newName,
            Path = storageKey,
            StorageProvider = activeProvider.Kind,
            ContentType = request.ContentType,
            SizeBytes = saveResult.SizeBytes,
            UploadedByUserId = uploaderId
        };
        await unitOfWork.GetRepository<Domain.Calendars.CalendarEventDocument>().InsertAsync(document);

        await logWriter.WriteAsync(calendarEvent, Domain.Calendars.Enums.CalendarEventLogAction.DocumentAdded,
            new CalendarActor(webChannel.Id, uploaderId),
            change: null, notes: null,
            commentId: request.CalendarEventCommentId, documentId: document.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CalendarEventDocumentDto>
        {
            IsSuccess = true,
            Message = "Calendar document uploaded successfully.",
            Data = new CalendarEventDocumentDto
            {
                Id = document.Id,
                CalendarEventId = document.CalendarEventId,
                CalendarEventCommentId = document.CalendarEventCommentId,
                DocumentType = (int)document.DocumentType,
                OriginalName = document.OriginalName,
                NewName = document.NewName,
                ContentType = document.ContentType,
                SizeBytes = document.SizeBytes,
                UploadedByUserId = document.UploadedByUserId,
                CreatedAt = document.Created,
                IsDeleted = false
            }
        };
    }

    /// <summary>
    /// Maps the file extension to a <see cref="DocumentType"/> (same table as the ticket upload).
    /// </summary>
    public static bool TryMapExtension(string extension, out DocumentType documentType)
    {
        documentType = DocumentType.None;
        return extension switch
        {
            ".pdf" => Assign(DocumentType.Pdf, out documentType),
            ".doc" or ".docx" => Assign(DocumentType.Word, out documentType),
            ".xls" or ".xlsx" => Assign(DocumentType.Excel, out documentType),
            ".txt" or ".csv" => Assign(DocumentType.Text, out documentType),
            ".png" or ".jpg" or ".jpeg" => Assign(DocumentType.Image, out documentType),
            ".zip" => Assign(DocumentType.Other, out documentType),
            _ => false
        };

        static bool Assign(DocumentType value, out DocumentType slot)
        {
            slot = value;
            return true;
        }
    }
}