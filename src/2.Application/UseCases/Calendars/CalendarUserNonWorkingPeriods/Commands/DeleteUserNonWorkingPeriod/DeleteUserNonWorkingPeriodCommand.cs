using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.DeleteUserNonWorkingPeriod;

/// <summary>
/// Deletes a non-working period of any user of the company.
/// </summary>
public record DeleteUserNonWorkingPeriodCommand(Guid Id) : ITransactionalCommand<Response<bool>>;
