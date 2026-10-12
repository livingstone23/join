using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Calendars;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Builds the company catalog DTOs from tracked entities (write responses). The read side selects the same shape
/// with Dapper (<see cref="SelectActivityCompanyColumns"/>, <see cref="SelectStatusCompanyColumns"/>).
/// </summary>
public static class CalendarCompanyCatalogDtos
{
    public const string SelectActivityCompanyColumns = """
        ac.Id, ac.CompanyId, ac.CalendarActivityId, a.Code, a.Name AS GlobalName, a.IsActive AS GlobalIsActive,
        ac.Name, ac.Color, a.Icon, ac.DefaultDurationMinutes, ac.AllowsOverlap, ac.RequiresBusinessHours,
        ac.InitialCalendarStatusCompanyId, isc.Name AS InitialStatusName, ac.IsActive, ac.SortOrder, ac.Created AS CreatedAt
        FROM Calendar.ActivityCompanies ac
        INNER JOIN Calendar.Activities a ON a.Id = ac.CalendarActivityId
        LEFT JOIN Calendar.StatusCompanies isc ON isc.Id = ac.InitialCalendarStatusCompanyId
        """;

    public const string SelectStatusCompanyColumns = """
        sc.Id, sc.CompanyId, sc.CalendarStatusId, s.Code, s.Name AS GlobalName, s.IsActive AS GlobalIsActive,
        sc.Name, sc.Color, s.BlocksTime, s.IsFinal, sc.IsActive, sc.SortOrder, sc.Created AS CreatedAt
        FROM Calendar.StatusCompanies sc
        INNER JOIN Calendar.Statuses s ON s.Id = sc.CalendarStatusId
        """;

    public static CalendarActivityCompanyDto ToDto(CalendarActivityCompany entity, CalendarActivity global, string? initialStatusName) => new()
    {
        Id = entity.Id,
        CompanyId = entity.CompanyId,
        CalendarActivityId = entity.CalendarActivityId,
        Code = global.Code,
        GlobalName = global.Name,
        GlobalIsActive = global.IsActive && global.GcRecord == 0,
        Name = entity.Name,
        Color = entity.Color,
        Icon = global.Icon,
        DefaultDurationMinutes = entity.DefaultDurationMinutes,
        AllowsOverlap = entity.AllowsOverlap,
        RequiresBusinessHours = entity.RequiresBusinessHours,
        InitialCalendarStatusCompanyId = entity.InitialCalendarStatusCompanyId,
        InitialStatusName = initialStatusName,
        IsActive = entity.IsActive,
        SortOrder = entity.SortOrder,
        CreatedAt = entity.Created
    };

    public static CalendarStatusCompanyDto ToDto(CalendarStatusCompany entity, CalendarStatus global) => new()
    {
        Id = entity.Id,
        CompanyId = entity.CompanyId,
        CalendarStatusId = entity.CalendarStatusId,
        Code = global.Code,
        GlobalName = global.Name,
        GlobalIsActive = global.IsActive && global.GcRecord == 0,
        Name = entity.Name,
        Color = entity.Color,
        BlocksTime = global.BlocksTime,
        IsFinal = global.IsFinal,
        IsActive = entity.IsActive,
        SortOrder = entity.SortOrder,
        CreatedAt = entity.Created
    };
}
