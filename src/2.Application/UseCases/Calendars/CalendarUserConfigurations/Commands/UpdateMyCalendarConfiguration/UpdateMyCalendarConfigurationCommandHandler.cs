using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateMyCalendarConfiguration;

/// <summary>
/// Handles <see cref="UpdateMyCalendarConfigurationCommand"/>.
/// </summary>
public sealed class UpdateMyCalendarConfigurationCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarCompanyGuard companyGuard,
    CalendarUserDefaultCoordinator defaultCoordinator)
    : IRequestHandler<UpdateMyCalendarConfigurationCommand, Response<CalendarUserConfigurationDto>>
{
    public async Task<Response<CalendarUserConfigurationDto>> Handle(UpdateMyCalendarConfigurationCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarUserConfigurationDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var userId))
        {
            return Response<CalendarUserConfigurationDto>.Error(CalendarErrorCodes.UserRequired, ["The authenticated user could not be resolved."]);
        }

        var company = await companyGuard.GetContextAsync(companyId, cancellationToken);
        if (!company.IsSuccess)
        {
            return Response<CalendarUserConfigurationDto>.Error(company.Message, company.Errors);
        }

        var configuration = await defaultCoordinator.GetConfigurationAsync(companyId, userId);
        if (configuration is null)
        {
            return Response<CalendarUserConfigurationDto>.Error(CalendarErrorCodes.UserHasNoCalendar, ["You have no calendar in this company yet."]);
        }

        configuration.DefaultView = request.DefaultView;
        configuration.WeekStartsOn = request.WeekStartsOn;
        await unitOfWork.GetRepository<CalendarUserConfiguration>().UpdateAsync(configuration);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var user = await unitOfWork.GetRepository<ApplicationUser>().GetAsync(userId);
        var calendar = await unitOfWork.GetRepository<UserCalendar>().GetAsync(configuration.DefaultCalendarId);
        return new Response<CalendarUserConfigurationDto>
        {
            IsSuccess = true,
            Message = "Calendar preferences updated successfully.",
            Data = CalendarUserConfigurationDtos.From(configuration, user, calendar?.Name, company.Data!)
        };
    }
}
