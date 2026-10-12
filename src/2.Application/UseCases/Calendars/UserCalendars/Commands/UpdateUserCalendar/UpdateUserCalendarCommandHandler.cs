using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.UserCalendars.Commands.UpdateUserCalendar;

/// <summary>
/// Handles <see cref="UpdateUserCalendarCommand"/>: deactivating follows the same blocks as deleting.
/// </summary>
public sealed class UpdateUserCalendarCommandHandler(IUnitOfWork unitOfWork, UserCalendarCommandContext context)
    : IRequestHandler<UpdateUserCalendarCommand, Response<UserCalendarDto>>
{
    public async Task<Response<UserCalendarDto>> Handle(UpdateUserCalendarCommand request, CancellationToken cancellationToken)
    {
        var opened = await context.OpenAsync(cancellationToken);
        if (!opened.IsSuccess)
        {
            return Response<UserCalendarDto>.Error(opened.Message, opened.Errors);
        }

        var (companyId, userId) = opened.Data;
        var calendar = await context.FindOwnAsync(companyId, userId, request.Id);
        if (calendar is null)
        {
            return Response<UserCalendarDto>.Error(CalendarErrorCodes.CalendarNotFound, ["The calendar was not found."]);
        }

        var name = request.Name.Trim();
        if (await context.IsNameTakenAsync(companyId, userId, name, calendar.Id))
        {
            return Response<UserCalendarDto>.Error(CalendarErrorCodes.CalendarNameDuplicate, ["You already have a calendar with this name."]);
        }

        if (calendar.IsActive && !request.IsActive)
        {
            var retire = await context.CanRetireAsync(calendar, cancellationToken);
            if (!retire.IsSuccess)
            {
                return Response<UserCalendarDto>.Error(retire.Message, retire.Errors);
            }
        }

        calendar.Name = name;
        calendar.Description = CalendarCatalogRules.Clean(request.Description);
        calendar.Color = request.Color.Trim();
        calendar.IsActive = request.IsActive;
        calendar.SortOrder = request.SortOrder;
        await unitOfWork.GetRepository<UserCalendar>().UpdateAsync(calendar);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<UserCalendarDto> { IsSuccess = true, Data = await context.ToDtoAsync(calendar), Message = "Calendar updated successfully." };
    }
}
