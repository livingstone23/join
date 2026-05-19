using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Customers.Queries;

/// <summary>
/// Handles paginated customer queries using Dapper.
/// </summary>
public sealed class GetCustomersPagedQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCustomersPagedQuery, Response<PagedResult<CustomerResponseDto>>>
{
    private const int MaxPageSize = 50;
    private const int DefaultPageSize = 10;

    public async Task<Response<PagedResult<CustomerResponseDto>>> Handle(
        GetCustomersPagedQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<CustomerResponseDto>>.Error(
                "COMPANY_REQUIRED",
                ["The X-Company-Id header is required."]);
        }

        var sanitizedPageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var sanitizedPageSize = request.PageSize < 1 ? DefaultPageSize : Math.Min(request.PageSize, MaxPageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        using var connection = connectionFactory.CreateConnection();

        var parameters = new DynamicParameters();
        parameters.Add("TenantId", currentUserService.CompanyId);
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE cust.CompanyId = @TenantId AND cust.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.CustomerCode))
        {
            whereBuilder.Append(" AND cust.CustomerCode LIKE @CustomerCode");
            parameters.Add("CustomerCode", $"%{request.CustomerCode.Trim()}%");
        }

        if (request.PersonLifecycleStage.HasValue)
        {
            whereBuilder.Append(" AND cust.PersonLifecycleStage = @PersonLifecycleStage");
            parameters.Add("PersonLifecycleStage", (int)request.PersonLifecycleStage.Value);
        }

        if (request.IsActive.HasValue)
        {
            whereBuilder.Append(" AND cust.IsActive = @IsActive");
            parameters.Add("IsActive", request.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.PersonName))
        {
            whereBuilder.Append("""
                 AND (
                    CASE
                        WHEN p.PersonType = 1 THEN LTRIM(RTRIM(CONCAT(
                            p.FirstName, ' ',
                            ISNULL(p.MiddleName + ' ', ''),
                            ISNULL(p.LastName, ''), ' ',
                            ISNULL(p.SecondLastName, ''))))
                        ELSE COALESCE(NULLIF(p.CommercialName, ''), p.FirstName, '')
                    END
                ) LIKE @PersonName
                """);
            parameters.Add("PersonName", $"%{request.PersonName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(request.UserEmail))
        {
            whereBuilder.Append(" AND u.Email LIKE @UserEmail");
            parameters.Add("UserEmail", $"%{request.UserEmail.Trim()}%");
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            {CustomerQuerySql.SelectProjection}
            {whereClause}
            ORDER BY cust.Created DESC, cust.CustomerCode ASC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Admin.Customers cust
            INNER JOIN Admin.Persons p
                ON p.Id = cust.PersonId
               AND p.CompanyId = @TenantId
               AND p.GcRecord = 0
            INNER JOIN Security.Users u
                ON u.Id = cust.UserId
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<CustomerResponseDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<CustomerResponseDto>>
        {
            IsSuccess = true,
            Message = "Customers retrieved successfully.",
            Data = new PagedResult<CustomerResponseDto>
            {
                Items = items,
                PageNumber = sanitizedPageNumber,
                PageSize = sanitizedPageSize,
                TotalCount = totalCount,
                TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)sanitizedPageSize)
            }
        };
    }

    private static string GetPaginationClause(IDbConnection connection)
        => connection.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? "LIMIT @PageSize OFFSET @Offset"
            : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
}
