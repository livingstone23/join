using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;

/// <summary>
/// SPEC 69 — handles <see cref="DeleteCalendarEventCommentCommand"/>: the comment must belong to the route's activity
/// in the current tenant (otherwise <c>CALENDAR_COMMENT_NOT_FOUND</c>), must not be deleted yet
/// (<c>CALENDAR_RECORD_ALREADY_DELETED</c>) and its activity must be visible (rule 5). Soft-deletes it with the
/// reason and writes the <c>CommentDeleted</c> log row with the reason in <c>Notes</c>.
/// </summary>
public sealed class DeleteCalendarEventCommentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarEventRecordsAccess access,
    CalendarEventLogWriter logWriter)
    : IRequestHandler<DeleteCalendarEventCommentCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(DeleteCalendarEventCommentCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUserService.UserId, out var actorId) || actorId == Guid.Empty)
        {
            return Response<bool>.Error(CalendarErrorCodes.UserRequired,
                ["The authenticated user could not be resolved."]);
        }

        var comment = (await unitOfWork.GetRepository<Domain.Calendars.CalendarEventComment>()
            .GetAllIncludingDeletedAsync(x => x.Id == request.CommentId))
            .FirstOrDefault();

        if (comment is null
            || comment.GcRecord != 0
            || comment.CompanyId != currentUserService.CompanyId
            || comment.CalendarEventId != request.CalendarEventId)
        {
            return Response<bool>.Error(CalendarErrorCodes.CommentNotFound,
                ["The comment does not exist on this activity in the current tenant."]);
        }

        if (comment.DeletedUtc is not null)
        {
            return Response<bool>.Error(CalendarErrorCodes.RecordAlreadyDeleted,
                ["The comment has already been deleted."]);
        }

        // The activity must still be visible to the current user: a user who lost the flag mid-life still
        // cannot delete someone else's comment through this endpoint.
        var eventAccess = await access.ResolveAsync(comment.CalendarEventId, cancellationToken);
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
        comment.DeletedByUserId = actorId;
        comment.DeletedUtc = now;
        comment.DeleteReason = request.Reason.Trim();
        await unitOfWork.GetRepository<Domain.Calendars.CalendarEventComment>().UpdateAsync(comment);

        await logWriter.WriteAsync(calendarEvent, Domain.Calendars.Enums.CalendarEventLogAction.CommentDeleted,
            new CalendarActor(webChannel.Id, actorId),
            change: null, notes: comment.DeleteReason,
            commentId: comment.Id, documentId: null);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<bool> { IsSuccess = true, Message = "Calendar comment deleted successfully.", Data = true };
    }
}