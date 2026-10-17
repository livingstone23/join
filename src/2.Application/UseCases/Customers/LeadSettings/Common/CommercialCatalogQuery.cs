using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.Services.Customers;

namespace JOIN.Application.UseCases.Customers.LeadSettings.Common;

/// <summary>
/// Dapper reads shared by the commercial catalogs (SPEC 59): every row of the company ordered by <c>SortOrder</c>,
/// without paging (they are short catalogs).
/// </summary>
internal static class CommercialCatalogQuery
{
    private static string Select(string table, string extraColumns) => $"""
        SELECT
            t.Id,
            t.GcRecord,
            t.CompanyId,
            t.Code,
            t.Name,
            t.SortOrder,
            t.IsActive,
            t.Created AS CreatedAt{extraColumns}
        FROM customer.{table} t
        """;

    internal static async Task<Response<IReadOnlyList<TDto>>> ListAsync<TDto>(
        ISqlConnectionFactory connectionFactory,
        ICurrentUserService currentUserService,
        string table,
        string extraColumns,
        bool? includeInactive,
        bool? includeDeleted,
        Guid? companyId,
        string label,
        CancellationToken cancellationToken,
        string extraWhere = "",
        Func<Guid, object>? parameters = null)
        where TDto : CommercialCatalogItemDto
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return CompanyRequired<IReadOnlyList<TDto>>();
        }

        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, includeDeleted) ? string.Empty : "AND t.GcRecord = 0";
        var inactive = includeInactive == false ? "AND t.IsActive = TRUE" : string.Empty;
        var sql = $"""
            {Select(table, extraColumns)}
            WHERE t.CompanyId = @CompanyId
              {deleted}
              {inactive}
              {extraWhere}
            ORDER BY t.SortOrder, t.Name;
            """;

        var tenantId = TenantResolver.Resolve(currentUserService, companyId);
        var arguments = parameters?.Invoke(tenantId) ?? new { CompanyId = tenantId };

        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<TDto>(new CommandDefinition(sql, arguments, cancellationToken: cancellationToken))).ToList();

        return new Response<IReadOnlyList<TDto>> { IsSuccess = true, Message = $"{label} retrieved successfully.", Data = items };
    }

    internal static async Task<Response<TDto>> GetByIdAsync<TDto>(
        ISqlConnectionFactory connectionFactory,
        ICurrentUserService currentUserService,
        string table,
        string extraColumns,
        Guid id,
        bool? includeDeleted,
        Guid? companyId,
        string notFoundCode,
        string label,
        CancellationToken cancellationToken)
        where TDto : CommercialCatalogItemDto
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return CompanyRequired<TDto>();
        }

        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, includeDeleted) ? string.Empty : "AND t.GcRecord = 0";
        var sql = $"""
            {Select(table, extraColumns)}
            WHERE t.Id = @Id
              AND t.CompanyId = @CompanyId
              {deleted};
            """;

        using var connection = connectionFactory.CreateConnection();
        var item = await connection.QuerySingleOrDefaultAsync<TDto>(new CommandDefinition(
            sql, new { Id = id, CompanyId = TenantResolver.Resolve(currentUserService, companyId) }, cancellationToken: cancellationToken));

        return item is null
            ? Response<TDto>.Error(notFoundCode, [$"The {label} was not found."])
            : new Response<TDto> { IsSuccess = true, Message = $"The {label} was retrieved successfully.", Data = item };
    }

    internal static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(LeadSettingsErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
}
