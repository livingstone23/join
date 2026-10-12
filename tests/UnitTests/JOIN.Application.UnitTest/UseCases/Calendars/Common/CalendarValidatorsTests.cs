using FluentAssertions;
using FluentValidation;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.CreateCalendarActivity;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.DeleteCalendarActivity;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.UpdateCalendarActivity;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.DeleteCalendarActivityCompany;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.EnableCalendarActivityCompany;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.UpdateCalendarActivityCompany;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.CreateCalendarCompany;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.ProvisionCalendarUsers;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.SyncCalendarCatalogs;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.UpdateCalendarCompany;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.CreateCalendarCompanyNonWorkingDay;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.DeleteCalendarCompanyNonWorkingDay;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.UpdateCalendarCompanyNonWorkingDay;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.ChangeCalendarEventStatus;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.CreateCalendarEvent;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.DeleteCalendarEvent;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.UpdateCalendarEvent;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.ChangeCalendarPanelEventStatus;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.CreateCalendarPanelEvent;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.DeleteCalendarPanelEvent;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.UpdateCalendarPanelEvent;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.DeleteCalendarStatusCompany;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.EnableCalendarStatusCompany;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.UpdateCalendarStatusCompany;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.CreateCalendarStatus;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.DeleteCalendarStatus;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.UpdateCalendarStatus;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateMyCalendarConfiguration;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateUserCalendarConfiguration;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.CreateMyNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.CreateUserNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.DeleteMyNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.DeleteUserNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.UpdateUserNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.CreateUserCalendar;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.DeleteUserCalendar;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.SetDefaultUserCalendar;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.UpdateUserCalendar;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UnitTest.UseCases.Calendars.Common;

/// <summary>
/// SPEC 44 — FluentValidation rules of every calendar command: an empty command fails, a valid one passes.
/// </summary>
public sealed class CalendarValidatorsTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2027, 3, 1, 10, 0, 0, TimeSpan.FromHours(-6));

    public static TheoryData<IValidator, object, object> Cases => new()
    {
        { new CreateCalendarActivityCommandValidator(), new CreateCalendarActivityCommand { Code = "bad code!", Color = "blue", DefaultDurationMinutes = 1, SortOrder = -1 },
            new CreateCalendarActivityCommand { Code = "VISIT", Name = "Visita", Color = "#123456", DefaultDurationMinutes = 30 } },
        { new UpdateCalendarActivityCommandValidator(), new UpdateCalendarActivityCommand(),
            new UpdateCalendarActivityCommand { Id = Id, Name = "Visita", Color = "#123456", DefaultDurationMinutes = 30 } },
        { new DeleteCalendarActivityCommandValidator(), new DeleteCalendarActivityCommand(Guid.Empty), new DeleteCalendarActivityCommand(Id) },
        { new CreateCalendarStatusCommandValidator(), new CreateCalendarStatusCommand(), new CreateCalendarStatusCommand { Code = "X", Name = "X", Color = "#123456" } },
        { new UpdateCalendarStatusCommandValidator(), new UpdateCalendarStatusCommand(), new UpdateCalendarStatusCommand { Id = Id, Name = "X", Color = "#123456" } },
        { new DeleteCalendarStatusCommandValidator(), new DeleteCalendarStatusCommand(Guid.Empty), new DeleteCalendarStatusCommand(Id) },
        { new EnableCalendarActivityCompanyCommandValidator(), new EnableCalendarActivityCompanyCommand(), new EnableCalendarActivityCompanyCommand { CalendarActivityId = Id } },
        { new UpdateCalendarActivityCompanyCommandValidator(), new UpdateCalendarActivityCompanyCommand(),
            new UpdateCalendarActivityCompanyCommand { Id = Id, Name = "X", Color = "#123456", DefaultDurationMinutes = 30, InitialCalendarStatusCompanyId = Id } },
        { new DeleteCalendarActivityCompanyCommandValidator(), new DeleteCalendarActivityCompanyCommand(Guid.Empty), new DeleteCalendarActivityCompanyCommand(Id) },
        { new EnableCalendarStatusCompanyCommandValidator(), new EnableCalendarStatusCompanyCommand(), new EnableCalendarStatusCompanyCommand { CalendarStatusId = Id } },
        { new UpdateCalendarStatusCompanyCommandValidator(), new UpdateCalendarStatusCompanyCommand(), new UpdateCalendarStatusCompanyCommand { Id = Id, Name = "X", Color = "#123456" } },
        { new DeleteCalendarStatusCompanyCommandValidator(), new DeleteCalendarStatusCompanyCommand(Guid.Empty), new DeleteCalendarStatusCompanyCommand(Id) },
        { new CreateCalendarCompanyCommandValidator(), new CreateCalendarCompanyCommand(), new CreateCalendarCompanyCommand { DefaultActivityUserId = Id, TimeZone = "America/Managua" } },
        { new UpdateCalendarCompanyCommandValidator(), new UpdateCalendarCompanyCommand(), new UpdateCalendarCompanyCommand { DefaultActivityUserId = Id, TimeZone = "America/Managua" } },
        { new CreateCalendarCompanyNonWorkingDayCommandValidator(), new CreateCalendarCompanyNonWorkingDayCommand(),
            new CreateCalendarCompanyNonWorkingDayCommand { Date = new DateOnly(2027, 9, 15), Name = "Independencia" } },
        { new UpdateCalendarCompanyNonWorkingDayCommandValidator(), new UpdateCalendarCompanyNonWorkingDayCommand(),
            new UpdateCalendarCompanyNonWorkingDayCommand { Id = Id, Date = new DateOnly(2027, 9, 15), Name = "Independencia" } },
        { new DeleteCalendarCompanyNonWorkingDayCommandValidator(), new DeleteCalendarCompanyNonWorkingDayCommand(Guid.Empty), new DeleteCalendarCompanyNonWorkingDayCommand(Id) },
        { new UpdateMyCalendarConfigurationCommandValidator(), new UpdateMyCalendarConfigurationCommand { DefaultView = (CalendarViewType)9, WeekStartsOn = DayOfWeek.Friday },
            new UpdateMyCalendarConfigurationCommand() },
        { new UpdateUserCalendarConfigurationCommandValidator(),
            new UpdateUserCalendarConfigurationCommand { BusinessHoursStart = new TimeOnly(10, 0), BusinessHoursEnd = new TimeOnly(9, 0), WorkDays = CalendarWorkDays.None },
            new UpdateUserCalendarConfigurationCommand { UserId = Id, BusinessHoursStart = new TimeOnly(9, 0), BusinessHoursEnd = new TimeOnly(12, 0) } },
        { new CreateMyNonWorkingPeriodCommandValidator(), new CreateMyNonWorkingPeriodCommand { StartDate = new DateOnly(2027, 3, 2), EndDate = new DateOnly(2027, 3, 1), Reason = (CalendarNonWorkingReason)5 },
            new CreateMyNonWorkingPeriodCommand { StartDate = new DateOnly(2027, 3, 1), EndDate = new DateOnly(2027, 3, 1) } },
        { new CreateUserNonWorkingPeriodCommandValidator(), new CreateUserNonWorkingPeriodCommand(),
            new CreateUserNonWorkingPeriodCommand { UserId = Id, StartDate = new DateOnly(2027, 3, 1), EndDate = new DateOnly(2027, 3, 1) } },
        { new UpdateUserNonWorkingPeriodCommandValidator(), new UpdateUserNonWorkingPeriodCommand(),
            new UpdateUserNonWorkingPeriodCommand { Id = Id, StartDate = new DateOnly(2027, 3, 1), EndDate = new DateOnly(2027, 3, 1) } },
        { new DeleteMyNonWorkingPeriodCommandValidator(), new DeleteMyNonWorkingPeriodCommand(Guid.Empty), new DeleteMyNonWorkingPeriodCommand(Id) },
        { new DeleteUserNonWorkingPeriodCommandValidator(), new DeleteUserNonWorkingPeriodCommand(Guid.Empty), new DeleteUserNonWorkingPeriodCommand(Id) },
        { new CreateUserCalendarCommandValidator(), new CreateUserCalendarCommand(), new CreateUserCalendarCommand { Name = "Ventas", Color = "#123456" } },
        { new UpdateUserCalendarCommandValidator(), new UpdateUserCalendarCommand(), new UpdateUserCalendarCommand { Id = Id, Name = "Ventas", Color = "#123456" } },
        { new DeleteUserCalendarCommandValidator(), new DeleteUserCalendarCommand(Guid.Empty), new DeleteUserCalendarCommand(Id) },
        { new SetDefaultUserCalendarCommandValidator(), new SetDefaultUserCalendarCommand(Guid.Empty), new SetDefaultUserCalendarCommand(Id) },
        { new CreateCalendarEventCommandValidator(), new CreateCalendarEventCommand { VideoCallUrl = "http://insecure" },
            new CreateCalendarEventCommand { CalendarActivityCompanyId = Id, Title = "x", Start = Start, VideoCallUrl = "https://meet.example/abc" } },
        { new UpdateCalendarEventCommandValidator(), new UpdateCalendarEventCommand { Scope = (CalendarRecurrenceEditScope)7, IsAllDay = true },
            new UpdateCalendarEventCommand { Id = Id, CalendarActivityCompanyId = Id, Title = "x", IsAllDay = true, StartDate = new DateOnly(2027, 3, 1) } },
        { new ChangeCalendarEventStatusCommandValidator(), new ChangeCalendarEventStatusCommand(), new ChangeCalendarEventStatusCommand { Id = Id, CalendarStatusCompanyId = Id } },
        { new DeleteCalendarEventCommandValidator(), new DeleteCalendarEventCommand(Guid.Empty), new DeleteCalendarEventCommand(Id, CalendarRecurrenceEditScope.Series) },
        { new CreateCalendarPanelEventCommandValidator(), new CreateCalendarPanelEventCommand(),
            new CreateCalendarPanelEventCommand { OwnerUserId = Id, CalendarActivityCompanyId = Id, Title = "x", Start = Start } },
        { new UpdateCalendarPanelEventCommandValidator(), new UpdateCalendarPanelEventCommand(),
            new UpdateCalendarPanelEventCommand { Id = Id, CalendarActivityCompanyId = Id, Title = "x", Start = Start } },
        { new ChangeCalendarPanelEventStatusCommandValidator(), new ChangeCalendarPanelEventStatusCommand(), new ChangeCalendarPanelEventStatusCommand { Id = Id, CalendarStatusCompanyId = Id } },
        { new DeleteCalendarPanelEventCommandValidator(), new DeleteCalendarPanelEventCommand(Guid.Empty), new DeleteCalendarPanelEventCommand(Id) }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Validator_ShouldRejectInvalidAndAcceptValidCommands(IValidator validator, object invalid, object valid)
    {
        validator.Validate(new ValidationContext<object>(invalid)).IsValid.Should().BeFalse();
        var result = validator.Validate(new ValidationContext<object>(valid));
        result.IsValid.Should().BeTrue(string.Join(" | ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void BodilessValidators_ShouldAcceptTheCommands()
    {
        new SyncCalendarCatalogsCommandValidator().Validate(new SyncCalendarCatalogsCommand()).IsValid.Should().BeTrue();
        new ProvisionCalendarUsersCommandValidator().Validate(new ProvisionCalendarUsersCommand()).IsValid.Should().BeTrue();
    }
}
