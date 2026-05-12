using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CustomerAddresses.Queries;

/// <summary>
/// Handles customer address listing requests using Dapper for read-optimized access.
/// </summary>
public sealed class GetCustomerAddressesByCustomerIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCustomerAddressesByCustomerIdQuery, Response<List<CustomerAddressResponseDto>>>
{
    /// <summary>
    /// Retrieves customer addresses from SQL using tenant and soft-delete filters.
    /// </summary>
    /// <param name="request">The request carrying the customer identifier.</param>
    /// <param name="cancellationToken">A cancellation token for query execution.</param>
    /// <returns>A response containing the list of customer addresses.</returns>
    public async Task<Response<List<CustomerAddressResponseDto>>> Handle(
        GetCustomerAddressesByCustomerIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<List<CustomerAddressResponseDto>>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                a.Id,
                a.CustomerId,
                a.AddressLine1,
                a.AddressLine2,
                a.ZipCode,
                a.StreetTypeId,
                a.CountryId,
                a.RegionId,
                a.ProvinceId,
                a.MunicipalityId,
                a.IsDefault
            FROM Admin.CustomerAddresses a
            WHERE a.CustomerId = @CustomerId
              AND a.CompanyId = @CompanyId
              AND a.GcRecord = 0
            ORDER BY a.IsDefault DESC, a.Created DESC;
            """;

        var rows = await connection.QueryAsync<CustomerAddressResponseDto>(
            new CommandDefinition(
                sql,
                new
                {
                    request.CustomerId,
                    CompanyId = currentUserService.CompanyId
                },
                cancellationToken: cancellationToken));

        return new Response<List<CustomerAddressResponseDto>>
        {
            IsSuccess = true,
            Message = "Customer addresses retrieved successfully.",
            Data = rows.ToList()
        };
    }
}
