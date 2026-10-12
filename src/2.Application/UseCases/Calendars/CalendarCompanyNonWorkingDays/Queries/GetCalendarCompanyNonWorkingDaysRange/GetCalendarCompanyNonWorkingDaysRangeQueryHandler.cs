using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Queries.GetCalendarCompanyNonWorkingDaysRange;

/// <summary>
/// Handles <see cref="GetCalendarCompanyNonWorkingDaysRangeQuery"/>: at most 366 days.
/// </summary>
public sealed class GetCalendarCompanyNonWorkingDaysRangeQueryHandler(
    ISqlConnectionFactory connectionFactory,
    CalendarQueryReader reader,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarCompanyNonWorkingDaysRangeQuery, Response<IReadOnlyList<CalendarCompanyNonWorkingDayDto>>>
{
    public async Task<Response<IReadOnlyList<CalendarCompanyNonWorkingDayDto>>> Handle(GetCalendarCompanyNonWorkingDaysRangeQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyList<CalendarCompanyNonWorkingDayDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (request.To < request.From || request.To.DayNumber - request.From.DayNumber > 366)
        {
            return Response<IReadOnlyList<CalendarCompanyNonWorkingDayDto>>.Error(CalendarErrorCodes.RangeTooLarge, ["'to' must be on or after 'from' and the range cannot exceed 366 days."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var holidays = await reader.GetHolidaysAsync(connection, currentUserService.CompanyId, request.From, request.To, cancellationToken);
        return new Response<IReadOnlyList<CalendarCompanyNonWorkingDayDto>> { IsSuccess = true, Data = holidays, Message = "Holidays retrieved successfully." };
    }
}
