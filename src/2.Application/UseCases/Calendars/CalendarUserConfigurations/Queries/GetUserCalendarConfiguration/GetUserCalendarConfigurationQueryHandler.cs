using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Queries.GetUserCalendarConfiguration;

/// <summary>
/// Handles <see cref="GetUserCalendarConfigurationQuery"/> through <see cref="CalendarUserConfigurationQuery"/>.
/// </summary>
public sealed class GetUserCalendarConfigurationQueryHandler(CalendarUserConfigurationQuery query)
    : IRequestHandler<GetUserCalendarConfigurationQuery, Response<CalendarUserConfigurationDto>>
{
    public Task<Response<CalendarUserConfigurationDto>> Handle(GetUserCalendarConfigurationQuery request, CancellationToken cancellationToken) =>
        query.GetAsync(request.UserId, cancellationToken);
}
