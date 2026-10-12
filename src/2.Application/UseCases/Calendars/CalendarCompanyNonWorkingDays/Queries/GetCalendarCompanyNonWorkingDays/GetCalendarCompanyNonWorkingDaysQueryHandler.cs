using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Queries.GetCalendarCompanyNonWorkingDays;

/// <summary>
/// Handles <see cref="GetCalendarCompanyNonWorkingDaysQuery"/> with Dapper (<see cref="CalendarQueryReader.GetHolidaysAsync"/>);
/// the year is small, so it is paged in memory after resolving the recurring holidays.
/// </summary>
public sealed class GetCalendarCompanyNonWorkingDaysQueryHandler(
    ISqlConnectionFactory connectionFactory,
    CalendarQueryReader reader,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarCompanyNonWorkingDaysQuery, Response<PagedResult<CalendarCompanyNonWorkingDayDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<CalendarCompanyNonWorkingDayDto>>> Handle(GetCalendarCompanyNonWorkingDaysQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<CalendarCompanyNonWorkingDayDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var year = request.Year is >= 1900 and <= 9998 ? request.Year.Value : DateTime.UtcNow.Year;
        var (pageNumber, pageSize) = _pagination.Sanitize(request.PageNumber, request.PageSize);

        using var connection = connectionFactory.CreateConnection();
        var holidays = await reader.GetHolidaysAsync(connection, currentUserService.CompanyId, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), cancellationToken);
        var items = holidays.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

        return CalendarPaging.Page(items, pageNumber, pageSize, holidays.Count, "Holidays retrieved successfully.");
    }
}
