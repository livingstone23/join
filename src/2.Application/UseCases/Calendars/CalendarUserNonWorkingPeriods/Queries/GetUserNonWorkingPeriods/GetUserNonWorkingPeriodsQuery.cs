using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Queries.GetUserNonWorkingPeriods;

/// <summary>
/// Paged non-working periods of the company, optionally of one user and one year.
/// </summary>
public record GetUserNonWorkingPeriodsQuery(Guid? UserId = null, int? Year = null, int? PageNumber = null, int? PageSize = null)
    : IRequest<Response<PagedResult<CalendarUserNonWorkingPeriodDto>>>;
