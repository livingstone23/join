using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.CreateUserNonWorkingPeriod;

/// <summary>
/// Registers a non-working period of another user of the company (<c>CalendarSettings</c>).
/// </summary>
public record CreateUserNonWorkingPeriodCommand : ITransactionalCommand<Response<CalendarNonWorkingPeriodResultDto>>, INonWorkingPeriodInput
{
    public Guid UserId { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public CalendarNonWorkingReason Reason { get; init; } = CalendarNonWorkingReason.Vacation;
    public string? Notes { get; init; }
}
