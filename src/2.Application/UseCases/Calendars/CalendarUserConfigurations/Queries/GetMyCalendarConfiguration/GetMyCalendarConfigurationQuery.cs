using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Queries.GetMyCalendarConfiguration;

/// <summary>
/// The current user's configuration with the effective hours and the company data.
/// </summary>
public record GetMyCalendarConfigurationQuery : IRequest<Response<CalendarUserConfigurationDto>>;
