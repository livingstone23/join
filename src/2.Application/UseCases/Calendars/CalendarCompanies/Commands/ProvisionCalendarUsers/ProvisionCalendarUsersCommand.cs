using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.ProvisionCalendarUsers;

/// <summary>
/// Creates the default calendar of the users of the current company still pending provisioning.
/// </summary>
public record ProvisionCalendarUsersCommand : ITransactionalCommand<Response<IReadOnlyList<CalendarProvisionedUserDto>>>;
