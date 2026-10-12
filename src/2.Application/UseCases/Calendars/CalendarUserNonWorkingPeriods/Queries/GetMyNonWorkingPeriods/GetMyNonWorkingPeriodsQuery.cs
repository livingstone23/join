using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Queries.GetMyNonWorkingPeriods;

/// <summary>
/// The current user's non-working periods that touch a year (current year by default).
/// </summary>
public record GetMyNonWorkingPeriodsQuery(int? Year = null) : IRequest<Response<IReadOnlyList<CalendarUserNonWorkingPeriodDto>>>;
