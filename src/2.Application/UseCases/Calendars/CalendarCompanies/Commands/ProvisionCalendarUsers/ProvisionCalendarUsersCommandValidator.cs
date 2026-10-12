using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.ProvisionCalendarUsers;

/// <summary>
/// <see cref="ProvisionCalendarUsersCommand"/> has no body; the tenant comes from the token.
/// </summary>
public sealed class ProvisionCalendarUsersCommandValidator : AbstractValidator<ProvisionCalendarUsersCommand>
{
}
