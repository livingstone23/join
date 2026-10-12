using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.SyncCalendarCatalogs;

/// <summary>
/// Copies to the current company the global activity types and statuses it does not have yet.
/// </summary>
public record SyncCalendarCatalogsCommand : ITransactionalCommand<Response<CalendarCatalogSyncDto>>;
