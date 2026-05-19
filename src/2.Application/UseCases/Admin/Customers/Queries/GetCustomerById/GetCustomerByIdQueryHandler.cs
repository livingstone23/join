using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Customers.Queries;

/// <summary>
/// Handles single customer retrieval using Dapper.
/// </summary>
public sealed class GetCustomerByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCustomerByIdQuery, Response<CustomerResponseDto>>
{
    public async Task<Response<CustomerResponseDto>> Handle(
        GetCustomerByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CustomerResponseDto>.Error(
                "COMPANY_REQUIRED",
                ["The X-Company-Id header is required."]);
        }

        using var connection = connectionFactory.CreateConnection();

        var sql = $"""
            {CustomerQuerySql.SelectProjection}
            WHERE cust.Id = @Id
              AND cust.CompanyId = @TenantId
              AND cust.GcRecord = 0;
            """;

        var customer = await connection.QuerySingleOrDefaultAsync<CustomerResponseDto>(
            new CommandDefinition(
                sql,
                new
                {
                    request.Id,
                    TenantId = currentUserService.CompanyId
                },
                cancellationToken: cancellationToken));

        if (customer is null)
        {
            return Response<CustomerResponseDto>.Error(
                "CUSTOMER_NOT_FOUND",
                ["Customer not found."]);
        }

        return new Response<CustomerResponseDto>
        {
            IsSuccess = true,
            Message = "Customer retrieved successfully.",
            Data = customer
        };
    }
}
