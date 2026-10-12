using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Queries.GetUserCalendarConfiguration;

/// <summary>
/// The configuration of one user of the company.
/// </summary>
public record GetUserCalendarConfigurationQuery(Guid UserId) : IRequest<Response<CalendarUserConfigurationDto>>;
