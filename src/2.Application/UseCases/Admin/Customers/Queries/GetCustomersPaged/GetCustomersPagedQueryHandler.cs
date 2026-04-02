using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Customers.Queries;



/// <summary>
/// Handles paginated customer queries using Dapper for high-performance read operations.
/// </summary>
/// <param name="connectionFactory">The factory used to create database connections.</param>
/// <param name="currentUserService">The current user context used to resolve the tenant company.</param>
public class GetCustomersPagedQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCustomersPagedQuery, Response<PagedResult<CustomerListItemDto>>>
{
    private const int MaxPageSize = 50;
    private const int DefaultPageSize = 10;

    /// <summary>
    /// Retrieves a paginated list of customers filtered by the current tenant.
    /// </summary>
    /// <param name="request">The pagination request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A standardized paginated response with customer list items.</returns>
    public async Task<Response<PagedResult<CustomerListItemDto>>> Handle(GetCustomersPagedQuery request, CancellationToken cancellationToken)
    {
        var response = new Response<PagedResult<CustomerListItemDto>>();

        if (currentUserService.CompanyId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.Message = "CompanyId header or claim is required.";
            response.Errors = ["The X-Company-Id header is required."];
            return response;
        }

        var sanitizedPageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var sanitizedPageSize = request.PageSize < 1 ? DefaultPageSize : Math.Min(request.PageSize, MaxPageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        using var connection = connectionFactory.CreateConnection();

        var parameters = new DynamicParameters();
        parameters.Add("TenantId", currentUserService.CompanyId);
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE c.CompanyId = @TenantId AND c.GcRecord = 0");

        AddLikeFilter(request.PersonType, "PersonType");
        AddLikeFilter(request.FirstName, "FirstName");
        AddLikeFilter(request.MiddleName, "MiddleName");
        AddLikeFilter(request.LastName, "LastName");
        AddLikeFilter(request.SecondLastName, "SecondLastName");
        AddLikeFilter(request.CommercialName, "CommercialName");
        AddLikeFilter(request.IdentificationNumber, "IdentificationNumber");

        if (request.IdentificationTypeId.HasValue && request.IdentificationTypeId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND c.IdentificationTypeId = @IdentificationTypeId");
            parameters.Add("IdentificationTypeId", request.IdentificationTypeId.Value);
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                c.Id,
                c.CompanyId,
                c.PersonType,
                c.FirstName,
                c.MiddleName,
                c.LastName,
                c.SecondLastName,
                c.CommercialName,
                c.IdentificationTypeId,
                it.Name AS IdentificationTypeName,
                c.IdentificationNumber
            FROM Admin.Customers c
            LEFT JOIN Admin.IdentificationTypes it ON c.IdentificationTypeId = it.Id
            {whereClause}
            ORDER BY c.Created DESC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Admin.Customers c
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(
                sql,
                parameters,
                cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<CustomerListItemDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        response.Data = new PagedResult<CustomerListItemDto>
        {
            Items = items,
            PageNumber = sanitizedPageNumber,
            PageSize = sanitizedPageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)sanitizedPageSize)
        };
        response.IsSuccess = true;
        response.Message = "Customers retrieved successfully.";

        return response;

        void AddLikeFilter(string? value, string columnName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            whereBuilder.Append($" AND c.{columnName} LIKE @{columnName}");
            parameters.Add(columnName, $"%{value.Trim()}%");
        }
    }

    /// <summary>
    /// Resolves a provider-compatible pagination clause.
    /// </summary>
    /// <param name="connection">The active database connection.</param>
    /// <returns>The pagination SQL fragment for the current provider.</returns>
    private static string GetPaginationClause(IDbConnection connection)
        => connection.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? "LIMIT @PageSize OFFSET @Offset"
            : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
}