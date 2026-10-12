using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelUsers;

/// <summary>
/// Handles <see cref="GetCalendarPanelUsersQuery"/> with Dapper: only active members of the current company.
/// </summary>
public sealed class GetCalendarPanelUsersQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarPanelUsersQuery, Response<PagedResult<CalendarPanelUserDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<CalendarPanelUserDto>>> Handle(GetCalendarPanelUsersQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<CalendarPanelUserDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var (pageNumber, pageSize) = _pagination.Sanitize(request.PageNumber, request.PageSize);
        var parameters = new DynamicParameters();
        parameters.Add("TenantId", currentUserService.CompanyId);
        parameters.Add("Offset", (pageNumber - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder("WHERE uc.CompanyId = @TenantId AND uc.GcRecord = 0 AND u.GcRecord = 0 AND u.IsActive = TRUE");
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            where.Append(" AND (CONCAT(u.FirstName, ' ', u.LastName) ILIKE @Search OR u.Email ILIKE @Search)");
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        if (request.OnlyWithCalendar == true)
        {
            where.Append(" AND c.Id IS NOT NULL");
        }

        var from = $"""
            FROM Security.UserCompanies uc
            INNER JOIN Security.Users u ON u.Id = uc.UserId
            LEFT JOIN Calendar.UserConfigurations c ON c.UserId = uc.UserId AND c.CompanyId = @TenantId AND c.GcRecord = 0
            {where}
            """;

        var sql = $"""
            SELECT u.Id AS UserId, CONCAT(u.FirstName, ' ', u.LastName) AS Name, u.Email, (c.Id IS NOT NULL) AS HasCalendar
            {from}
            ORDER BY u.FirstName, u.LastName
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*) {from};
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<CalendarPanelUserDto>()).Select(u => u with { Name = u.Name.Trim() }).ToList();
        var total = await multi.ReadSingleAsync<int>();

        return CalendarPaging.Page(items, pageNumber, pageSize, total, "Panel users retrieved successfully.");
    }
}
