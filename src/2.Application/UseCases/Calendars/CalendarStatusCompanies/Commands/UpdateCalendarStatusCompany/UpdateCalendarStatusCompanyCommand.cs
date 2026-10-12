using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.UpdateCalendarStatusCompany;

/// <summary>
/// Edits a status of the company (name, color, active flag, order). <c>BlocksTime</c> and <c>IsFinal</c> are global.
/// </summary>
public record UpdateCalendarStatusCompanyCommand : ITransactionalCommand<Response<CalendarStatusCompanyDto>>
{
    [JsonIgnore]
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
    public int SortOrder { get; init; }
}
