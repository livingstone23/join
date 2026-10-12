using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Calendars;
using Riok.Mapperly.Abstractions;

namespace JOIN.Application.Mappings;

/// <summary>
/// Source-generated mapper of the calendar module. Only target members are required: entities carry audit and
/// navigation members the DTOs do not expose.
/// </summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target, EnumMappingStrategy = EnumMappingStrategy.ByValue)]
public partial class CalendarMapper : ICalendarMapper
{
    /// <inheritdoc />
    [MapProperty(nameof(CalendarActivity.Created), nameof(CalendarActivityDto.CreatedAt))]
    public partial CalendarActivityDto ToDto(CalendarActivity entity);

    /// <inheritdoc />
    [MapProperty(nameof(CalendarStatus.Created), nameof(CalendarStatusDto.CreatedAt))]
    public partial CalendarStatusDto ToDto(CalendarStatus entity);

    /// <inheritdoc />
    [MapProperty(nameof(UserCalendar.Created), nameof(UserCalendarDto.CreatedAt))]
    [MapperIgnoreTarget(nameof(UserCalendarDto.OwnerName))]
    [MapperIgnoreTarget(nameof(UserCalendarDto.IsDefault))]
    public partial UserCalendarDto ToDto(UserCalendar entity);

    /// <inheritdoc />
    [MapProperty(nameof(CalendarCompanyNonWorkingDay.Created), nameof(CalendarCompanyNonWorkingDayDto.CreatedAt))]
    [MapProperty(nameof(CalendarCompanyNonWorkingDay.Date), nameof(CalendarCompanyNonWorkingDayDto.OccursOn))]
    public partial CalendarCompanyNonWorkingDayDto ToDto(CalendarCompanyNonWorkingDay entity);

    /// <inheritdoc />
    [MapProperty(nameof(CalendarUserNonWorkingPeriod.Created), nameof(CalendarUserNonWorkingPeriodDto.CreatedAt))]
    [MapperIgnoreTarget(nameof(CalendarUserNonWorkingPeriodDto.UserName))]
    public partial CalendarUserNonWorkingPeriodDto ToDto(CalendarUserNonWorkingPeriod entity);
}
