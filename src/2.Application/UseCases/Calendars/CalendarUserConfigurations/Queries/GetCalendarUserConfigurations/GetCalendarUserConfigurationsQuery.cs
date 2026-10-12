using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Queries.GetCalendarUserConfigurations;

/// <summary>
/// Paged users of the company with their configuration, including the ones without calendar.
/// </summary>
public record GetCalendarUserConfigurationsQuery(string? Search = null, int? PageNumber = null, int? PageSize = null)
    : IRequest<Response<PagedResult<CalendarUserConfigurationDto>>>;
