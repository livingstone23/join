using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonAddresses.Queries;

/// <summary>
/// Handles customer address listing requests using Dapper for read-optimized access.
/// </summary>
public sealed class GetPersonAddressesByPersonIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonAddressesByPersonIdQuery, Response<List<PersonAddressResponseDto>>>
{
    /// <summary>
    /// Retrieves customer addresses from SQL using tenant and soft-delete filters.
    /// </summary>
    /// <param name="request">The request carrying the customer identifier.</param>
    /// <param name="cancellationToken">A cancellation token for query execution.</param>
    /// <returns>A response containing the list of customer addresses.</returns>
    public async Task<Response<List<PersonAddressResponseDto>>> Handle(
        GetPersonAddressesByPersonIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<List<PersonAddressResponseDto>>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                a.Id,
                a.PersonId,
                a.AddressLine1,
                a.AddressLine2,
                a.ZipCode,
                a.StreetTypeId,
                a.CountryId,
                a.RegionId,
                a.ProvinceId,
                a.MunicipalityId,
                a.IsDefault
            FROM Admin.PersonAddresses a
            WHERE a.PersonId = @PersonId
              AND a.CompanyId = @CompanyId
              AND a.GcRecord = 0
            ORDER BY a.IsDefault DESC, a.Created DESC;
            """;

        var rows = await connection.QueryAsync<PersonAddressResponseDto>(
            new CommandDefinition(
                sql,
                new
                {
                    request.PersonId,
                    CompanyId = currentUserService.CompanyId
                },
                cancellationToken: cancellationToken));

        return new Response<List<PersonAddressResponseDto>>
        {
            IsSuccess = true,
            Message = "Person addresses retrieved successfully.",
            Data = rows.ToList()
        };
    }
}
