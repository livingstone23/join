using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Queries.GetCalendarStatusCompanies;

/// <summary>
/// Handles <see cref="GetCalendarStatusCompaniesQuery"/> with Dapper.
/// </summary>
public sealed class GetCalendarStatusCompaniesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarStatusCompaniesQuery, Response<PagedResult<CalendarStatusCompanyDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<CalendarStatusCompanyDto>>> Handle(GetCalendarStatusCompaniesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<CalendarStatusCompanyDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var (pageNumber, pageSize) = _pagination.Sanitize(request.PageNumber, request.PageSize);
        var parameters = new DynamicParameters();
        parameters.Add("TenantId", currentUserService.CompanyId);
        parameters.Add("Offset", (pageNumber - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder("WHERE sc.CompanyId = @TenantId AND sc.GcRecord = 0");
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            where.Append(" AND (s.Code ILIKE @Search OR sc.Name ILIKE @Search)");
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        if (request.IsActive.HasValue)
        {
            where.Append(" AND sc.IsActive = @IsActive");
            parameters.Add("IsActive", request.IsActive.Value);
        }

        var sql = $"""
            SELECT {CalendarCompanyCatalogDtos.SelectStatusCompanyColumns}
            {where}
            ORDER BY sc.SortOrder, sc.Name
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM Calendar.StatusCompanies sc
            INNER JOIN Calendar.Statuses s ON s.Id = sc.CalendarStatusId
            {where};
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<CalendarStatusCompanyDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return CalendarPaging.Page(items, pageNumber, pageSize, total, "Company statuses retrieved successfully.");
    }
}
