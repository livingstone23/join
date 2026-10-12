using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.UserCalendars.Commands.SetDefaultUserCalendar;

/// <summary>
/// Handles <see cref="SetDefaultUserCalendarCommand"/> through <see cref="CalendarUserDefaultCoordinator"/>.
/// </summary>
public sealed class SetDefaultUserCalendarCommandHandler(
    IUnitOfWork unitOfWork,
    UserCalendarCommandContext context,
    CalendarUserDefaultCoordinator defaultCoordinator)
    : IRequestHandler<SetDefaultUserCalendarCommand, Response<UserCalendarDto>>
{
    public async Task<Response<UserCalendarDto>> Handle(SetDefaultUserCalendarCommand request, CancellationToken cancellationToken)
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

        var result = await defaultCoordinator.SetDefaultAsync(calendar);
        if (!result.IsSuccess)
        {
            return Response<UserCalendarDto>.Error(result.Message, result.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<UserCalendarDto> { IsSuccess = true, Data = await context.ToDtoAsync(calendar) with { IsDefault = true }, Message = "Default calendar changed successfully." };
    }
}
