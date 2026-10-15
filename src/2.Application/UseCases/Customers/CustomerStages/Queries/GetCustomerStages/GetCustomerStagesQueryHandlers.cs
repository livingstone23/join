using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Customers.CustomerStages.Queries;

/// <summary>
/// Shared SQL of the customer stage queries.
/// </summary>
internal static class CustomerStageQuerySql
{
    internal const string Select = """
        SELECT
            s.Id,
            s.GcRecord,
            s.CompanyId,
            s.Code,
            s.Name,
            s.Description,
            s.SortOrder,
            s.IsDefault,
            s.IsActive,
            s.Created AS CreatedAt,
            (SELECT COUNT(*)::int
             FROM customer.customers c
             WHERE c.CustomerStageId = s.Id
               AND c.GcRecord = 0) AS CustomersCount
        FROM customer.customerstages s
        """;
}

/// <summary>
/// Handles <see cref="GetCustomerStagesQuery"/>.
/// </summary>
public sealed class GetCustomerStagesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCustomerStagesQuery, Response<IReadOnlyList<CustomerStageDto>>>
{
    public async Task<Response<IReadOnlyList<CustomerStageDto>>> Handle(GetCustomerStagesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyList<CustomerStageDto>>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted)
            ? string.Empty
            : "AND s.GcRecord = 0";
        var inactive = request.IncludeInactive == true ? string.Empty : "AND s.IsActive = TRUE";

        var sql = $"""
            {CustomerStageQuerySql.Select}
            WHERE s.CompanyId = @CompanyId
              {deleted}
              {inactive}
            ORDER BY s.SortOrder ASC, s.Name ASC;
            """;

        using var connection = connectionFactory.CreateConnection();
        var stages = (await connection.QueryAsync<CustomerStageDto>(new CommandDefinition(
                sql,
                new { CompanyId = TenantResolver.Resolve(currentUserService, request.CompanyId) },
                cancellationToken: cancellationToken)))
            .ToList();

        return new Response<IReadOnlyList<CustomerStageDto>>
        {
            IsSuccess = true,
            Message = "Customer stages retrieved successfully.",
            Data = stages
        };
    }
}

/// <summary>
/// Handles <see cref="GetCustomerStageByIdQuery"/>.
/// </summary>
public sealed class GetCustomerStageByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCustomerStageByIdQuery, Response<CustomerStageDto>>
{
    public async Task<Response<CustomerStageDto>> Handle(GetCustomerStageByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CustomerStageDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted)
            ? string.Empty
            : "AND s.GcRecord = 0";

        var sql = $"""
            {CustomerStageQuerySql.Select}
            WHERE s.Id = @Id
              AND s.CompanyId = @CompanyId
              {deleted};
            """;

        using var connection = connectionFactory.CreateConnection();
        var stage = await connection.QuerySingleOrDefaultAsync<CustomerStageDto>(new CommandDefinition(
            sql,
            new { request.Id, CompanyId = TenantResolver.Resolve(currentUserService, request.CompanyId) },
            cancellationToken: cancellationToken));

        return stage is null
            ? Response<CustomerStageDto>.Error("CUSTOMER_STAGE_NOT_FOUND", ["Customer stage not found."])
            : new Response<CustomerStageDto>
            {
                IsSuccess = true,
                Message = "Customer stage retrieved successfully.",
                Data = stage
            };
    }
}
