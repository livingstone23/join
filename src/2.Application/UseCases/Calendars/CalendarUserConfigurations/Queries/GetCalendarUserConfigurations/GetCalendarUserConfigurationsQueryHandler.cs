using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Queries.GetCalendarUserConfigurations;

/// <summary>
/// Handles <see cref="GetCalendarUserConfigurationsQuery"/> with Dapper.
/// </summary>
public sealed class GetCalendarUserConfigurationsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    CalendarQueryReader reader,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarUserConfigurationsQuery, Response<PagedResult<CalendarUserConfigurationDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<CalendarUserConfigurationDto>>> Handle(GetCalendarUserConfigurationsQuery request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<PagedResult<CalendarUserConfigurationDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var company = await reader.GetCalendarCompanyAsync(connection, companyId, cancellationToken);
        if (company is null)
        {
            return Response<PagedResult<CalendarUserConfigurationDto>>.Error(CalendarErrorCodes.CompanyNotConfigured, ["The company has no calendar configuration."]);
        }

        var (pageNumber, pageSize) = _pagination.Sanitize(request.PageNumber, request.PageSize);
        var parameters = new DynamicParameters();
        parameters.Add("TenantId", companyId);
        parameters.Add("Offset", (pageNumber - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder("WHERE uc.CompanyId = @TenantId AND uc.GcRecord = 0 AND u.GcRecord = 0");
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            where.Append(" AND (CONCAT(u.FirstName, ' ', u.LastName) ILIKE @Search OR u.Email ILIKE @Search)");
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        var sql = $"""
            SELECT {CalendarUserConfigurationDtos.SelectColumns}
            {where}
            ORDER BY u.FirstName, u.LastName
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM Security.UserCompanies uc
            INNER JOIN Security.Users u ON u.Id = uc.UserId
            {where};
            """;

        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<CalendarUserConfigurationDto>()).Select(row => CalendarUserConfigurationDtos.Enrich(row, company)).ToList();
        var total = await multi.ReadSingleAsync<int>();

        return CalendarPaging.Page(items, pageNumber, pageSize, total, "User calendar configurations retrieved successfully.");
    }
}
