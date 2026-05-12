using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CustomerContacts.Queries;

/// <summary>
/// Handles customer contact listing requests using Dapper for read-optimized access.
/// </summary>
public sealed class GetCustomerContactsByCustomerIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCustomerContactsByCustomerIdQuery, Response<List<CustomerContactResponseDto>>>
{
    /// <summary>
    /// Retrieves customer contacts from SQL using tenant and soft-delete filters.
    /// </summary>
    /// <param name="request">The request carrying the customer identifier.</param>
    /// <param name="cancellationToken">A cancellation token for query execution.</param>
    /// <returns>A response containing the list of customer contacts.</returns>
    public async Task<Response<List<CustomerContactResponseDto>>> Handle(
        GetCustomerContactsByCustomerIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<List<CustomerContactResponseDto>>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                c.Id,
                c.CustomerId,
                c.ContactType,
                c.ContactValue,
                c.IsPrimary,
                c.Comments,
                c.CompanyId
            FROM Admin.CustomerContacts c
            WHERE c.CustomerId = @CustomerId
              AND c.CompanyId = @CompanyId
              AND c.GcRecord = 0
            ORDER BY c.IsPrimary DESC, c.Created DESC;
            """;

        var rows = await connection.QueryAsync<CustomerContactResponseDto>(
            new CommandDefinition(
                sql,
                new
                {
                    request.CustomerId,
                    CompanyId = currentUserService.CompanyId
                },
                cancellationToken: cancellationToken));

        return new Response<List<CustomerContactResponseDto>>
        {
            IsSuccess = true,
            Message = "Customer contacts retrieved successfully.",
            Data = rows.ToList()
        };
    }
}
