using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateUserCalendarConfiguration;

/// <summary>
/// Handles <see cref="UpdateUserCalendarConfigurationCommand"/>. Own hours must fit in the company hours
/// (<c>CALENDAR_USER_HOURS_OUT_OF_COMPANY_RANGE</c>); in a 24-hour company any range of the day is valid.
/// </summary>
public sealed class UpdateUserCalendarConfigurationCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarCompanyGuard companyGuard,
    CalendarUserDefaultCoordinator defaultCoordinator)
    : IRequestHandler<UpdateUserCalendarConfigurationCommand, Response<CalendarUserConfigurationDto>>
{
    public async Task<Response<CalendarUserConfigurationDto>> Handle(UpdateUserCalendarConfigurationCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarUserConfigurationDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyResponse = await companyGuard.GetContextAsync(companyId, cancellationToken);
        if (!companyResponse.IsSuccess)
        {
            return Response<CalendarUserConfigurationDto>.Error(companyResponse.Message, companyResponse.Errors);
        }

        var company = companyResponse.Data!;
        var configuration = await defaultCoordinator.GetConfigurationAsync(companyId, request.UserId);
        if (configuration is null)
        {
            return Response<CalendarUserConfigurationDto>.Error(CalendarErrorCodes.UserConfigurationNotFound, ["The user has no calendar in this company yet."]);
        }

        if (request.BusinessHoursStart is { } start && request.BusinessHoursEnd is { } end
            && !company.IsOpen24Hours
            && (start < company.BusinessHoursStart || end > company.BusinessHoursEnd))
        {
            return Response<CalendarUserConfigurationDto>.Error(CalendarErrorCodes.UserHoursOutOfCompanyRange,
                [$"The user's hours must be inside the company hours ({company.BusinessHoursStart:HH\\:mm}-{company.BusinessHoursEnd:HH\\:mm})."]);
        }

        configuration.BusinessHoursStart = request.BusinessHoursStart;
        configuration.BusinessHoursEnd = request.BusinessHoursEnd;
        configuration.WorkDays = request.WorkDays;
        configuration.DefaultView = request.DefaultView;
        configuration.WeekStartsOn = request.WeekStartsOn;
        await unitOfWork.GetRepository<CalendarUserConfiguration>().UpdateAsync(configuration);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var user = await unitOfWork.GetRepository<ApplicationUser>().GetAsync(request.UserId);
        var calendar = await unitOfWork.GetRepository<UserCalendar>().GetAsync(configuration.DefaultCalendarId);
        return new Response<CalendarUserConfigurationDto>
        {
            IsSuccess = true,
            Message = "User calendar configuration updated successfully.",
            Data = CalendarUserConfigurationDtos.From(configuration, user, calendar?.Name, company)
        };
    }
}
