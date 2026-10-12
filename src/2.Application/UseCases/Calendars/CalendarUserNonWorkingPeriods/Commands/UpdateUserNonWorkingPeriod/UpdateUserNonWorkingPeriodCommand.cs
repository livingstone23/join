using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.UpdateUserNonWorkingPeriod;

/// <summary>
/// Updates a non-working period of a user of the company. The user of the period does not change.
/// </summary>
public record UpdateUserNonWorkingPeriodCommand : ITransactionalCommand<Response<CalendarNonWorkingPeriodResultDto>>, INonWorkingPeriodInput
{
    [JsonIgnore]
    public Guid Id { get; init; }

    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public CalendarNonWorkingReason Reason { get; init; } = CalendarNonWorkingReason.Vacation;
    public string? Notes { get; init; }
}
