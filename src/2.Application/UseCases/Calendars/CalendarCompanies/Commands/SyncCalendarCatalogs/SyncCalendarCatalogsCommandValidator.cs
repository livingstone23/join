using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.SyncCalendarCatalogs;

/// <summary>
/// <see cref="SyncCalendarCatalogsCommand"/> has no body; the tenant comes from the token.
/// </summary>
public sealed class SyncCalendarCatalogsCommandValidator : AbstractValidator<SyncCalendarCatalogsCommand>
{
}
