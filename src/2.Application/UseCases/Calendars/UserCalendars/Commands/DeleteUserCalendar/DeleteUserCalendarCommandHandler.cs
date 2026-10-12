using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.UserCalendars.Commands.DeleteUserCalendar;

/// <summary>
/// Handles <see cref="DeleteUserCalendarCommand"/> (<c>CALENDAR_IS_DEFAULT</c>, <c>CALENDAR_HAS_ACTIVE_EVENTS</c>).
/// </summary>
public sealed class DeleteUserCalendarCommandHandler(IUnitOfWork unitOfWork, UserCalendarCommandContext context)
    : IRequestHandler<DeleteUserCalendarCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(DeleteUserCalendarCommand request, CancellationToken cancellationToken)
    {
        var opened = await context.OpenAsync(cancellationToken);
        if (!opened.IsSuccess)
        {
            return Response<bool>.Error(opened.Message, opened.Errors);
        }

        var (companyId, userId) = opened.Data;
        var calendar = await context.FindOwnAsync(companyId, userId, request.Id);
        if (calendar is null)
        {
            return Response<bool>.Error(CalendarErrorCodes.CalendarNotFound, ["The calendar was not found."]);
        }

        var retire = await context.CanRetireAsync(calendar, cancellationToken);
        if (!retire.IsSuccess)
        {
            return retire;
        }

        calendar.MarkAsDeleted();
        await unitOfWork.GetRepository<UserCalendar>().UpdateAsync(calendar);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<bool> { IsSuccess = true, Data = true, Message = "Calendar deleted successfully." };
    }
}
