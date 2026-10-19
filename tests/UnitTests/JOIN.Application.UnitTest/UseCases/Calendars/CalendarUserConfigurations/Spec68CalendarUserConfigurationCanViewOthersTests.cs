using FluentAssertions;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarUserConfigurations;

/// <summary>
/// SPEC 68 — covers the read/write path of <c>CalendarUserConfiguration.CanViewOthersActivities</c> at the
/// projection level (the query returns the flag, the write response mirrors it) and confirms the default value of a
/// fresh configuration row is <c>false</c>.
/// </summary>
public sealed class Spec68CalendarUserConfigurationCanViewOthersTests
{
    /// <summary>
    /// A new entity defaults the flag to <c>false</c>: a user freshly provisioned cannot see other people's
    /// activities until an administrator turns it on.
    /// </summary>
    [Fact]
    public void NewEntity_DefaultsCanViewOthersActivitiesToFalse()
    {
        var configuration = new CalendarUserConfiguration
        {
            CompanyId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            DefaultCalendarId = Guid.NewGuid()
        };

        configuration.CanViewOthersActivities.Should().BeFalse();
    }

    /// <summary>
    /// <see cref="CalendarUserConfigurationDtos.From"/> propagates the flag to the DTO so the response of
    /// <c>PUT /CalendarUserConfigurations/{userId}</c> reflects the administrator's choice immediately.
    /// </summary>
    [Fact]
    public void From_PropagatesCanViewOthersActivitiesToDto()
    {
        var company = new CalendarCompanyContext(
            CompanyId: Guid.NewGuid(),
            CalendarCompanyId: Guid.NewGuid(),
            TimeZoneId: "America/Managua",
            TimeZone: TimeZoneInfo.Utc,
            IsOpen24Hours: true,
            BusinessHoursStart: null,
            BusinessHoursEnd: null,
            DefaultActivityUserId: Guid.NewGuid(),
            DefaultCalendarName: "Mi calendario",
            RescheduledByOthersStatusCompanyId: Guid.NewGuid());
        var configuration = new CalendarUserConfiguration
        {
            CompanyId = company.CompanyId,
            UserId = Guid.NewGuid(),
            DefaultCalendarId = Guid.NewGuid(),
            CanViewOthersActivities = true
        };

        var dto = CalendarUserConfigurationDtos.From(configuration, user: null, calendarName: null, company: company);

        dto.CanViewOthersActivities.Should().BeTrue();
    }

    /// <summary>
    /// The DTO row shape returned by the Dapper queries (<see cref="CalendarUserConfigurationDtos.SelectColumns"/>)
    /// exposes the flag with a <c>COALESCE</c> fallback so users without a configuration row still answer
    /// <c>false</c>.
    /// </summary>
    [Fact]
    public void SelectColumns_ExposesCanViewOthersActivitiesWithFalseFallback()
    {
        CalendarUserConfigurationDtos.SelectColumns.Should().Contain("CanViewOthersActivities");
        CalendarUserConfigurationDtos.SelectColumns.Should().Contain("COALESCE(c.CanViewOthersActivities, FALSE)");
    }

    /// <summary>
    /// The admin command accepts the flag; the self-update command does NOT, so the flag cannot leak through
    /// <c>PUT …/me</c> (SPEC 68 rule 4).
    /// </summary>
    [Fact]
    public void UpdateMyCalendarConfigurationCommand_DoesNotExposeCanViewOthersActivities()
    {
        var properties = typeof(JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateMyCalendarConfiguration.UpdateMyCalendarConfigurationCommand)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet();

        properties.Should().NotContain("CanViewOthersActivities");

        var adminProps = typeof(JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateUserCalendarConfiguration.UpdateUserCalendarConfigurationCommand)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet();

        adminProps.Should().Contain("CanViewOthersActivities");
    }

    /// <summary>
    /// <see cref="CalendarUserConfigurationDto"/> is a record: the new property must have a public setter or
    /// <c>init</c>-only accessor so the Dapper materializer can write it.
    /// </summary>
    [Fact]
    public void CalendarUserConfigurationDto_CanViewOthersActivitiesIsInitOnly()
    {
        var prop = typeof(CalendarUserConfigurationDto).GetProperty(nameof(CalendarUserConfigurationDto.CanViewOthersActivities));
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(bool));
    }
}