using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Queries.GetMyCalendarConfiguration;

/// <summary>
/// Handles <see cref="GetMyCalendarConfigurationQuery"/> through <see cref="CalendarUserConfigurationQuery"/>.
/// </summary>
public sealed class GetMyCalendarConfigurationQueryHandler(CalendarUserConfigurationQuery query, ICurrentUserService currentUserService)
    : IRequestHandler<GetMyCalendarConfigurationQuery, Response<CalendarUserConfigurationDto>>
{
    public Task<Response<CalendarUserConfigurationDto>> Handle(GetMyCalendarConfigurationQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUserService.UserId, out var userId))
        {
            return Task.FromResult(Response<CalendarUserConfigurationDto>.Error(CalendarErrorCodes.UserRequired, ["The authenticated user could not be resolved."]));
        }

        return query.GetAsync(userId, cancellationToken);
    }
}
