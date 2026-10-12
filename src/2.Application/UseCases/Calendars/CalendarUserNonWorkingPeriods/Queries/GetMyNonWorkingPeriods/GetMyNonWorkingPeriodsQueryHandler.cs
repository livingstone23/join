using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Queries.GetMyNonWorkingPeriods;

/// <summary>
/// Handles <see cref="GetMyNonWorkingPeriodsQuery"/> with Dapper (<see cref="CalendarQueryReader.GetPeriodsAsync"/>).
/// </summary>
public sealed class GetMyNonWorkingPeriodsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    CalendarQueryReader reader,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyNonWorkingPeriodsQuery, Response<IReadOnlyList<CalendarUserNonWorkingPeriodDto>>>
{
    public async Task<Response<IReadOnlyList<CalendarUserNonWorkingPeriodDto>>> Handle(GetMyNonWorkingPeriodsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyList<CalendarUserNonWorkingPeriodDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var userId))
        {
            return Response<IReadOnlyList<CalendarUserNonWorkingPeriodDto>>.Error(CalendarErrorCodes.UserRequired, ["The authenticated user could not be resolved."]);
        }

        var year = request.Year is >= 1900 and <= 9998 ? request.Year.Value : DateTime.UtcNow.Year;
        using var connection = connectionFactory.CreateConnection();
        var periods = await reader.GetPeriodsAsync(connection, currentUserService.CompanyId, [userId], new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), cancellationToken);

        return new Response<IReadOnlyList<CalendarUserNonWorkingPeriodDto>>
        {
            IsSuccess = true,
            Data = periods.OrderBy(p => p.StartDate).ToList(),
            Message = "Non-working periods retrieved successfully."
        };
    }
}
