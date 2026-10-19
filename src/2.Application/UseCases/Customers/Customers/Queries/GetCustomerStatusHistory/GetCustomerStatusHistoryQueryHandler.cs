using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;
using JOIN.Application.Interface;
using JOIN.Domain.Customers;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Customers.Customers.Queries;

/// <summary>
/// Handles <see cref="GetCustomerStatusHistoryQuery"/> with Dapper.
/// </summary>
public sealed class GetCustomerStatusHistoryQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetCustomerStatusHistoryQuery, Response<PagedResult<CustomerStatusHistoryDto>>>
{
    /// <summary>History page and total count; stage names come from the stage rows (SPEC 66). Public for the <c>EXPLAIN</c> test.</summary>
    public const string Sql = """
        SELECT
            h.Id,
            h.CustomerId,
            h.Action,
            h.Source,
            h.SourceReferenceId,
            ld.Code AS SourceReferenceCode,
            h.Notes,
            h.UserId,
            COALESCE(u.Email, '') AS UserEmail,
            h.OccurredAt,
            fs.Name AS FromStageName,
            ts.Name AS ToStageName
        FROM customer.customerstatushistory h
        LEFT JOIN Security.Users u
            ON u.Id = h.UserId
        LEFT JOIN customer.customerstages fs
            ON fs.Id = h.FromCustomerStageId
        LEFT JOIN customer.customerstages ts
            ON ts.Id = h.ToCustomerStageId
        LEFT JOIN customer.leads ld
            ON h.Source = 5
           AND ld.Id = h.SourceReferenceId
        WHERE h.CustomerId = @CustomerId
          AND h.CompanyId = @TenantId
          AND h.GcRecord = 0
        ORDER BY h.OccurredAt DESC, h.Id
        LIMIT @PageSize OFFSET @Offset;

        SELECT COUNT(*)
        FROM customer.customerstatushistory h
        WHERE h.CustomerId = @CustomerId
          AND h.CompanyId = @TenantId
          AND h.GcRecord = 0;
        """;

    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<CustomerStatusHistoryDto>>> Handle(
        GetCustomerStatusHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<CustomerStatusHistoryDto>>.Error(
                "COMPANY_REQUIRED",
                ["The X-Company-Id header is required."]);
        }

        var (pageNumber, pageSize) = _paginationSettings.Sanitize(request.PageNumber, request.PageSize);
        var parameters = new
        {
            request.CustomerId,
            TenantId = currentUserService.CompanyId,
            PageSize = pageSize,
            Offset = (pageNumber - 1) * pageSize
        };

        using var connection = connectionFactory.CreateConnection();

        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1 FROM customer.customers cust
                WHERE cust.Id = @CustomerId AND cust.CompanyId = @TenantId AND cust.GcRecord = 0);
            """,
            parameters,
            cancellationToken: cancellationToken));

        if (!exists)
        {
            return Response<PagedResult<CustomerStatusHistoryDto>>.Error("CUSTOMER_NOT_FOUND", ["Customer not found."]);
        }

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(Sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<CustomerStatusHistoryDto>())
            .Select(item => item with
            {
                ActionName = ((CustomerStatusAction)item.Action).ToString(),
                SourceName = ((CustomerStatusSource)item.Source).ToString(),
                OccurredAt = DateTime.SpecifyKind(item.OccurredAt, DateTimeKind.Utc)
            })
            .ToList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<CustomerStatusHistoryDto>>
        {
            IsSuccess = true,
            Message = "Customer status history retrieved successfully.",
            Data = new PagedResult<CustomerStatusHistoryDto>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
            }
        };
    }
}
