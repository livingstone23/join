using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Security;

namespace JOIN.Application.UseCases.Calendars.CalendarUserConfigurations;

/// <summary>
/// Builds <see cref="CalendarUserConfigurationDto"/> with the effective hours and the company data.
/// </summary>
public static class CalendarUserConfigurationDtos
{
    /// <summary> Columns shared by the configuration queries (one row per member of the company). </summary>
    public const string SelectColumns = """
        u.Id AS UserId,
        CONCAT(u.FirstName, ' ', u.LastName) AS UserName,
        u.Email,
        (c.Id IS NOT NULL) AS HasCalendar,
        c.Id,
        c.DefaultCalendarId,
        cal.Name AS DefaultCalendarName,
        c.BusinessHoursStart,
        c.BusinessHoursEnd,
        COALESCE(c.WorkDays, 0) AS WorkDays,
        COALESCE(c.DefaultView, 0) AS DefaultView,
        COALESCE(c.WeekStartsOn, 0) AS WeekStartsOn,
        COALESCE(c.CanViewOthersActivities, FALSE) AS CanViewOthersActivities
        FROM Security.UserCompanies uc
        INNER JOIN Security.Users u ON u.Id = uc.UserId
        LEFT JOIN Calendar.UserConfigurations c ON c.UserId = uc.UserId AND c.CompanyId = @TenantId AND c.GcRecord = 0
        LEFT JOIN Calendar.UserCalendars cal ON cal.Id = c.DefaultCalendarId
        """;

    /// <summary>
    /// Adds the company data and the effective hours to a row read with <see cref="SelectColumns"/>.
    /// </summary>
    public static CalendarUserConfigurationDto Enrich(CalendarUserConfigurationDto row, CalendarCompanyRow company)
    {
        var (start, end) = EffectiveHours(row.BusinessHoursStart, row.BusinessHoursEnd, company.IsOpen24Hours, company.BusinessHoursStart, company.BusinessHoursEnd);
        return row with
        {
            UserName = row.UserName.Trim(),
            EffectiveBusinessHoursStart = row.HasCalendar ? start : null,
            EffectiveBusinessHoursEnd = row.HasCalendar ? end : null,
            TimeZone = company.TimeZone,
            CompanyIsOpen24Hours = company.IsOpen24Hours,
            CompanyBusinessHoursStart = company.BusinessHoursStart,
            CompanyBusinessHoursEnd = company.BusinessHoursEnd
        };
    }

    /// <summary>
    /// Builds the DTO from tracked entities (write responses).
    /// </summary>
    public static CalendarUserConfigurationDto From(CalendarUserConfiguration configuration, ApplicationUser? user, string? calendarName, CalendarCompanyContext company)
    {
        var (start, end) = EffectiveHours(configuration.BusinessHoursStart, configuration.BusinessHoursEnd, company.IsOpen24Hours, company.BusinessHoursStart, company.BusinessHoursEnd);
        return new CalendarUserConfigurationDto
        {
            UserId = configuration.UserId,
            UserName = user is null ? string.Empty : $"{user.FirstName} {user.LastName}".Trim(),
            Email = user?.Email,
            HasCalendar = true,
            Id = configuration.Id,
            DefaultCalendarId = configuration.DefaultCalendarId,
            DefaultCalendarName = calendarName,
            BusinessHoursStart = configuration.BusinessHoursStart,
            BusinessHoursEnd = configuration.BusinessHoursEnd,
            WorkDays = (int)configuration.WorkDays,
            DefaultView = (int)configuration.DefaultView,
            WeekStartsOn = (int)configuration.WeekStartsOn,
            CanViewOthersActivities = configuration.CanViewOthersActivities,
            EffectiveBusinessHoursStart = start,
            EffectiveBusinessHoursEnd = end,
            TimeZone = company.TimeZoneId,
            CompanyIsOpen24Hours = company.IsOpen24Hours,
            CompanyBusinessHoursStart = company.BusinessHoursStart,
            CompanyBusinessHoursEnd = company.BusinessHoursEnd
        };
    }

    /// <summary>
    /// The user's own hours, otherwise the company's; <c>(00:00, null)</c> is the whole day.
    /// </summary>
    public static (TimeOnly Start, TimeOnly? End) EffectiveHours(TimeOnly? userStart, TimeOnly? userEnd, bool companyIs24Hours, TimeOnly? companyStart, TimeOnly? companyEnd)
    {
        if (userStart is { } ownStart && userEnd is { } ownEnd)
        {
            return (ownStart, ownEnd);
        }

        return companyIs24Hours || companyStart is null || companyEnd is null
            ? (TimeOnly.MinValue, null)
            : (companyStart.Value, companyEnd.Value);
    }
}
