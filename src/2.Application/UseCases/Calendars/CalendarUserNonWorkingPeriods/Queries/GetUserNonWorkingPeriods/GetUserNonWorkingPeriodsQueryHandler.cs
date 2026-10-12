using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Queries.GetUserNonWorkingPeriods;

/// <summary>
/// Handles <see cref="GetUserNonWorkingPeriodsQuery"/> with Dapper.
/// </summary>
public sealed class GetUserNonWorkingPeriodsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetUserNonWorkingPeriodsQuery, Response<PagedResult<CalendarUserNonWorkingPeriodDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<CalendarUserNonWorkingPeriodDto>>> Handle(GetUserNonWorkingPeriodsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<CalendarUserNonWorkingPeriodDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var (pageNumber, pageSize) = _pagination.Sanitize(request.PageNumber, request.PageSize);
        var parameters = new DynamicParameters();
        parameters.Add("TenantId", currentUserService.CompanyId);
        parameters.Add("Offset", (pageNumber - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder("WHERE p.CompanyId = @TenantId AND p.GcRecord = 0");
        if (request.UserId is { } userId)
        {
            where.Append(" AND p.UserId = @UserId");
            parameters.Add("UserId", userId);
        }

        if (request.Year is >= 1900 and <= 9998)
        {
            where.Append(" AND p.StartDate <= @YearEnd AND p.EndDate >= @YearStart");
            parameters.Add("YearStart", new DateTime(request.Year.Value, 1, 1));
            parameters.Add("YearEnd", new DateTime(request.Year.Value, 12, 31));
        }

        var sql = $"""
            SELECT p.Id, p.UserId, CONCAT(u.FirstName, ' ', u.LastName) AS UserName, p.StartDate, p.EndDate, p.Reason, p.Notes, p.Created AS CreatedAt
            FROM Calendar.UserNonWorkingPeriods p
            INNER JOIN Security.Users u ON u.Id = p.UserId
            {where}
            ORDER BY p.StartDate DESC
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*) FROM Calendar.UserNonWorkingPeriods p {where};
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<CalendarUserNonWorkingPeriodDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return CalendarPaging.Page(items, pageNumber, pageSize, total, "Non-working periods retrieved successfully.");
    }
}
