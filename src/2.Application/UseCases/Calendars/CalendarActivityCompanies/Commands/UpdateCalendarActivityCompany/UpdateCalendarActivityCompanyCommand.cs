using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.UpdateCalendarActivityCompany;

/// <summary>
/// Edits the effective values of an activity type in the company. Existing activities are not revalidated: the
/// changes apply to what is created or modified afterwards.
/// </summary>
public record UpdateCalendarActivityCompanyCommand : ITransactionalCommand<Response<CalendarActivityCompanyDto>>
{
    [JsonIgnore]
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public int DefaultDurationMinutes { get; init; }
    public bool AllowsOverlap { get; init; }
    public bool RequiresBusinessHours { get; init; }
    public Guid InitialCalendarStatusCompanyId { get; init; }
    public bool IsActive { get; init; } = true;
    public int SortOrder { get; init; }
}
