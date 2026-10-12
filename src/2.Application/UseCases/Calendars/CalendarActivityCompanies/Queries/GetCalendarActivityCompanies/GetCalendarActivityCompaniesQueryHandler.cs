using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Queries.GetCalendarActivityCompanies;

/// <summary>
/// Handles <see cref="GetCalendarActivityCompaniesQuery"/> with Dapper.
/// </summary>
public sealed class GetCalendarActivityCompaniesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarActivityCompaniesQuery, Response<PagedResult<CalendarActivityCompanyDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<CalendarActivityCompanyDto>>> Handle(GetCalendarActivityCompaniesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<CalendarActivityCompanyDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var (pageNumber, pageSize) = _pagination.Sanitize(request.PageNumber, request.PageSize);
        var parameters = new DynamicParameters();
        parameters.Add("TenantId", currentUserService.CompanyId);
        parameters.Add("Offset", (pageNumber - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder("WHERE ac.CompanyId = @TenantId AND ac.GcRecord = 0");
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            where.Append(" AND (a.Code ILIKE @Search OR ac.Name ILIKE @Search)");
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        if (request.IsActive.HasValue)
        {
            where.Append(" AND ac.IsActive = @IsActive");
            parameters.Add("IsActive", request.IsActive.Value);
        }

        var sql = $"""
            SELECT {CalendarCompanyCatalogDtos.SelectActivityCompanyColumns}
            {where}
            ORDER BY ac.SortOrder, ac.Name
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM Calendar.ActivityCompanies ac
            INNER JOIN Calendar.Activities a ON a.Id = ac.CalendarActivityId
            {where};
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<CalendarActivityCompanyDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return CalendarPaging.Page(items, pageNumber, pageSize, total, "Company activity types retrieved successfully.");
    }
}
