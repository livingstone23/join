using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.UserCalendars.Commands.CreateUserCalendar;

/// <summary>
/// Handles <see cref="CreateUserCalendarCommand"/>. The user must be provisioned first (a default calendar exists).
/// </summary>
public sealed class CreateUserCalendarCommandHandler(
    IUnitOfWork unitOfWork,
    UserCalendarCommandContext context,
    CalendarUserDefaultCoordinator defaultCoordinator)
    : IRequestHandler<CreateUserCalendarCommand, Response<UserCalendarDto>>
{
    public async Task<Response<UserCalendarDto>> Handle(CreateUserCalendarCommand request, CancellationToken cancellationToken)
    {
        var opened = await context.OpenAsync(cancellationToken);
        if (!opened.IsSuccess)
        {
            return Response<UserCalendarDto>.Error(opened.Message, opened.Errors);
        }

        var (companyId, userId) = opened.Data;
        if (await defaultCoordinator.GetConfigurationAsync(companyId, userId) is null)
        {
            return Response<UserCalendarDto>.Error(CalendarErrorCodes.UserHasNoCalendar, ["You have no calendar in this company yet (pending provisioning)."]);
        }

        var name = request.Name.Trim();
        if (await context.IsNameTakenAsync(companyId, userId, name, null))
        {
            return Response<UserCalendarDto>.Error(CalendarErrorCodes.CalendarNameDuplicate, ["You already have a calendar with this name."]);
        }

        var calendar = new UserCalendar
        {
            CompanyId = companyId,
            OwnerUserId = userId,
            Name = name,
            Description = CalendarCatalogRules.Clean(request.Description),
            Color = request.Color.Trim(),
            IsActive = true,
            SortOrder = request.SortOrder
        };

        await unitOfWork.GetRepository<UserCalendar>().InsertAsync(calendar);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<UserCalendarDto> { IsSuccess = true, Data = await context.ToDtoAsync(calendar), Message = "Calendar created successfully." };
    }
}
