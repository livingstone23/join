using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;

/// <summary>
/// SPEC 69 — handles <see cref="DeleteCalendarEventDocumentCommand"/>: the document must belong to the route's activity
/// in the current tenant (otherwise <c>CALENDAR_DOCUMENT_NOT_FOUND</c>), must not be deleted yet
/// (<c>CALENDAR_RECORD_ALREADY_DELETED</c>) and its activity must be visible (rule 5). Soft-deletes it with the
/// reason and writes the <c>DocumentDeleted</c> log row with the reason in <c>Notes</c>.
/// </summary>
public sealed class DeleteCalendarEventDocumentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarEventRecordsAccess access,
    CalendarEventLogWriter logWriter)
    : IRequestHandler<DeleteCalendarEventDocumentCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(DeleteCalendarEventDocumentCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUserService.UserId, out var actorId) || actorId == Guid.Empty)
        {
            return Response<bool>.Error(CalendarErrorCodes.UserRequired,
                ["The authenticated user could not be resolved."]);
        }

        var document = (await unitOfWork.GetRepository<Domain.Calendars.CalendarEventDocument>()
            .GetAllIncludingDeletedAsync(x => x.Id == request.DocumentId))
            .FirstOrDefault();

        if (document is null
            || document.GcRecord != 0
            || document.CompanyId != currentUserService.CompanyId
            || document.CalendarEventId != request.CalendarEventId)
        {
            return Response<bool>.Error(CalendarErrorCodes.DocumentNotFound,
                ["The document does not exist on this activity in the current tenant."]);
        }

        if (document.DeletedUtc is not null)
        {
            return Response<bool>.Error(CalendarErrorCodes.RecordAlreadyDeleted,
                ["The document has already been deleted."]);
        }

        var eventAccess = await access.ResolveAsync(document.CalendarEventId, cancellationToken);
        if (!eventAccess.IsFoundAndVisible)
        {
            return Response<bool>.Error(CalendarErrorCodes.EventNotFound,
                ["The calendar activity was not found or is not visible."]);
        }

        var calendarEvent = eventAccess.CalendarEvent!;
        var webChannel = (await unitOfWork.GetRepository<CommunicationChannel>()
            .GetAllIncludingDeletedAsync(x => x.Code == CommunicationChannelCodes.Web && x.GcRecord == 0))
            .FirstOrDefault();
        if (webChannel is null)
        {
            return Response<bool>.Error(CalendarErrorCodes.WebChannelNotFound,
                ["The WEB communication channel is missing (SPEC 00)."]);
        }

        var now = DateTime.UtcNow;
        document.DeletedByUserId = actorId;
        document.DeletedUtc = now;
        document.DeleteReason = request.Reason.Trim();
        await unitOfWork.GetRepository<Domain.Calendars.CalendarEventDocument>().UpdateAsync(document);

        await logWriter.WriteAsync(calendarEvent, Domain.Calendars.Enums.CalendarEventLogAction.DocumentDeleted,
            new CalendarActor(webChannel.Id, actorId),
            change: null, notes: document.DeleteReason,
            commentId: null, documentId: document.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<bool> { IsSuccess = true, Message = "Calendar document deleted successfully.", Data = true };
    }
}