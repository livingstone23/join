using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Calendars.CalendarActivities.Queries.GetCalendarActivities;

/// <summary>
/// Handles <see cref="GetCalendarActivitiesQuery"/> with Dapper. Global catalog: no tenant column, soft delete only.
/// </summary>
public sealed class GetCalendarActivitiesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarActivitiesQuery, Response<PagedResult<CalendarActivityDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<CalendarActivityDto>>> Handle(GetCalendarActivitiesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<CalendarActivityDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var (pageNumber, pageSize) = _pagination.Sanitize(request.PageNumber, request.PageSize);
        var parameters = new DynamicParameters();
        parameters.Add("Offset", (pageNumber - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder("WHERE a.GcRecord = 0");
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            where.Append(" AND (a.Code ILIKE @Search OR a.Name ILIKE @Search)");
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        if (request.IsActive.HasValue)
        {
            where.Append(" AND a.IsActive = @IsActive");
            parameters.Add("IsActive", request.IsActive.Value);
        }

        var sql = $"""
            SELECT a.Id, a.Code, a.Name, a.Description, a.Color, a.Icon, a.DefaultDurationMinutes,
                   a.AllowsOverlap, a.RequiresBusinessHours, a.IsActive, a.SortOrder, a.Created AS CreatedAt
            FROM Calendar.Activities a
            {where}
            ORDER BY a.SortOrder, a.Name
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*) FROM Calendar.Activities a {where};
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<CalendarActivityDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return CalendarPaging.Page(items, pageNumber, pageSize, total, "Calendar activity types retrieved successfully.");
    }
}
