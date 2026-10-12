using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.DeleteMyNonWorkingPeriod;

/// <summary>
/// Deletes a non-working period of the current user.
/// </summary>
public record DeleteMyNonWorkingPeriodCommand(Guid Id) : ITransactionalCommand<Response<bool>>;
