using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.CalendarActivityIndex;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;

/// <summary>
/// SPEC 69 — handles <see cref="CreateCalendarEventCommentCommand"/>: verifies the current user can read the
/// activity (rule 5), inserts the comment with the server clock and writes the <c>CommentAdded</c> log row. The
/// visibility verdict answers the same code on hidden activities as on missing ones, so the foreign-tenant
/// probe is indistinguishable.
/// </summary>
public sealed class CreateCalendarEventCommentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarActivityVisibility visibility,
    CalendarEventLogWriter logWriter)
    : IRequestHandler<CreateCalendarEventCommentCommand, Response<CalendarEventCommentDto>>
{
    public async Task<Response<CalendarEventCommentDto>> Handle(CreateCalendarEventCommentCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUserService.UserId, out var authorId) || authorId == Guid.Empty)
        {
            return Response<CalendarEventCommentDto>.Error(CalendarErrorCodes.UserRequired,
                ["The authenticated user could not be resolved."]);
        }

        var access = await visibility.ResolveEventAccessAsync(request.CalendarEventId, cancellationToken);
        if (!access.IsFoundAndVisible)
        {
            return Response<CalendarEventCommentDto>.Error(CalendarErrorCodes.EventNotFound,
                ["The calendar activity was not found or is not visible."]);
        }

        var calendarEvent = access.CalendarEvent!;
        var webChannel = (await unitOfWork.GetRepository<CommunicationChannel>()
            .GetAllIncludingDeletedAsync(x => x.Code == CommunicationChannelCodes.Web && x.GcRecord == 0))
            .FirstOrDefault();
        if (webChannel is null)
        {
            return Response<CalendarEventCommentDto>.Error(CalendarErrorCodes.WebChannelNotFound,
                ["The WEB communication channel is missing (SPEC 00)."]);
        }

        var now = DateTime.UtcNow;
        var comment = new Domain.Calendars.CalendarEventComment
        {
            CompanyId = calendarEvent.CompanyId,
            CalendarEventId = calendarEvent.Id,
            Body = request.Body.Trim(),
            AuthorUserId = authorId,
            CreatedUtc = now
        };
        await unitOfWork.GetRepository<Domain.Calendars.CalendarEventComment>().InsertAsync(comment);
        await logWriter.WriteAsync(calendarEvent, Domain.Calendars.Enums.CalendarEventLogAction.CommentAdded,
            new CalendarActor(webChannel.Id, authorId),
            change: null, notes: null,
            commentId: comment.Id, documentId: null);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var author = await unitOfWork.GetRepository<Domain.Security.ApplicationUser>().GetAsync(authorId);
        return new Response<CalendarEventCommentDto>
        {
            IsSuccess = true,
            Message = "Calendar comment added successfully.",
            Data = new CalendarEventCommentDto
            {
                Id = comment.Id,
                CalendarEventId = comment.CalendarEventId,
                AuthorUserId = comment.AuthorUserId,
                AuthorUserName = author is null ? string.Empty : $"{author.FirstName} {author.LastName}".Trim(),
                CreatedUtc = comment.CreatedUtc,
                Body = comment.Body,
                Documents = [],
                IsDeleted = false
            }
        };
    }
}