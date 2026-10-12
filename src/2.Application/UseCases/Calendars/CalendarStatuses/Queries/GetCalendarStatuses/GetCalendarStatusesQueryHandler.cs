using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Calendars.CalendarStatuses.Queries.GetCalendarStatuses;

/// <summary>
/// Handles <see cref="GetCalendarStatusesQuery"/> with Dapper. Global catalog: soft delete only.
/// </summary>
public sealed class GetCalendarStatusesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarStatusesQuery, Response<PagedResult<CalendarStatusDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<CalendarStatusDto>>> Handle(GetCalendarStatusesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<CalendarStatusDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var (pageNumber, pageSize) = _pagination.Sanitize(request.PageNumber, request.PageSize);
        var parameters = new DynamicParameters();
        parameters.Add("Offset", (pageNumber - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder("WHERE s.GcRecord = 0");
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            where.Append(" AND (s.Code ILIKE @Search OR s.Name ILIKE @Search)");
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        if (request.IsActive.HasValue)
        {
            where.Append(" AND s.IsActive = @IsActive");
            parameters.Add("IsActive", request.IsActive.Value);
        }

        var sql = $"""
            SELECT s.Id, s.Code, s.Name, s.Description, s.Color, s.BlocksTime, s.IsFinal, s.IsActive, s.SortOrder, s.Created AS CreatedAt
            FROM Calendar.Statuses s
            {where}
            ORDER BY s.SortOrder, s.Name
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*) FROM Calendar.Statuses s {where};
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<CalendarStatusDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return CalendarPaging.Page(items, pageNumber, pageSize, total, "Calendar statuses retrieved successfully.");
    }
}
