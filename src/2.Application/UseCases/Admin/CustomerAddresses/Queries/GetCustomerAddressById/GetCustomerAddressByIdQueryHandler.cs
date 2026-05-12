using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CustomerAddresses.Queries;

/// <summary>
/// Handles customer address detail queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create read connections.</param>
/// <param name="currentUserService">Service that exposes the active tenant identifier.</param>
public sealed class GetCustomerAddressByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCustomerAddressByIdQuery, Response<CustomerAddressResponseDto>>
{
    /// <summary>
    /// Retrieves a single address record restricted to the current tenant and active rows only.
    /// </summary>
    /// <param name="request">The query containing the address identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A response containing the requested address data.</returns>
    public async Task<Response<CustomerAddressResponseDto>> Handle(
        GetCustomerAddressByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CustomerAddressResponseDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                Id,
                CustomerId,
                AddressLine1,
                AddressLine2,
                ZipCode,
                StreetTypeId,
                CountryId,
                RegionId,
                ProvinceId,
                MunicipalityId,
                IsDefault,
                CompanyId
            FROM Admin.CustomerAddresses
            WHERE Id = @Id
              AND CompanyId = @CompanyId
              AND GcRecord = 0;
            """;

        var address = await connection.QuerySingleOrDefaultAsync<CustomerAddressResponseDto>(
            new CommandDefinition(
                sql,
                new
                {
                    request.Id,
                    CompanyId = currentUserService.CompanyId
                },
                cancellationToken: cancellationToken));

        if (address is null)
        {
            throw new NotFoundException(nameof(CustomerAddressResponseDto), request.Id, "Customer address not found.");
        }

        return new Response<CustomerAddressResponseDto>
        {
            IsSuccess = true,
            Message = "Customer address retrieved successfully.",
            Data = address
        };
    }
}