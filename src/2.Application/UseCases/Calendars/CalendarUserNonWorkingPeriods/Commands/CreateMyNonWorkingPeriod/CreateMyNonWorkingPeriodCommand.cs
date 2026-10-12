using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.CreateMyNonWorkingPeriod;

/// <summary>
/// Registers a non-working period of the current user.
/// </summary>
public record CreateMyNonWorkingPeriodCommand : ITransactionalCommand<Response<CalendarNonWorkingPeriodResultDto>>, INonWorkingPeriodInput
{
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public CalendarNonWorkingReason Reason { get; init; } = CalendarNonWorkingReason.Vacation;
    public string? Notes { get; init; }
}
