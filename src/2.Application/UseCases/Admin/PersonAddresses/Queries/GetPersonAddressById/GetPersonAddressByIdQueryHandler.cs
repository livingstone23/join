using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;



namespace JOIN.Application.UseCases.Admin.PersonAddresses.Queries;



/// <summary>
/// Handles person address detail queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create read connections.</param>
/// <param name="currentUserService">Service that exposes the active tenant identifier.</param>
public sealed class GetPersonAddressByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonAddressByIdQuery, Response<PersonAddressResponseDto>>
{
    /// <summary>
    /// Retrieves a single address record restricted to the current tenant and active rows only.
    /// </summary>
    /// <param name="request">The query containing the address identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A response containing the requested address data.</returns>
    public async Task<Response<PersonAddressResponseDto>> Handle(
        GetPersonAddressByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PersonAddressResponseDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        var sql = $"""
            {PersonAddressQuerySql.SelectWithCatalogNames}
            WHERE a.Id = @Id
              AND a.CompanyId = @CompanyId
              AND a.GcRecord = 0;
            """;

        var address = await connection.QuerySingleOrDefaultAsync<PersonAddressReadRow>(
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
            return Response<PersonAddressResponseDto>.Error(
                "PERSON_ADDRESS_NOT_FOUND",
                ["Person address not found."]);
        }

        return new Response<PersonAddressResponseDto>
        {
            IsSuccess = true,
            Message = "Person address retrieved successfully.",
            Data = PersonAddressQuerySql.ToResponseDto(address)
        };
    }
}