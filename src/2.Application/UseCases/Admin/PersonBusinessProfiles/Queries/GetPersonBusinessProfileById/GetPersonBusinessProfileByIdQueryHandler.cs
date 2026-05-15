using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Queries;

/// <summary>
/// Handles person business profile detail queries using Dapper with catalog joins.
/// </summary>
/// <param name="connectionFactory">Factory used to create read connections.</param>
/// <param name="currentUserService">Service that exposes the active tenant identifier.</param>
public sealed class GetPersonBusinessProfileByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonBusinessProfileByIdQuery, Response<PersonBusinessProfileResponseDto>>
{
    /// <summary>
    /// Retrieves a single business profile restricted to the current tenant and active rows only.
    /// </summary>
    /// <param name="request">The query containing the business profile identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A response containing the requested business profile data.</returns>
    public async Task<Response<PersonBusinessProfileResponseDto>> Handle(
        GetPersonBusinessProfileByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PersonBusinessProfileResponseDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                pbp.Id,
                pbp.PersonId,
                pbp.IndustryId,
                i.Name  AS IndustryName,
                pbp.TaxRegimeId,
                tr.Name AS TaxRegimeName,
                pbp.Website,
                pbp.FoundationDate,
                pbp.IsActive
            FROM Admin.PersonBusinessProfiles pbp
            INNER JOIN Admin.Industries i
                ON i.Id = pbp.IndustryId
               AND i.CompanyId = @CompanyId
               AND i.GcRecord = 0
            INNER JOIN Admin.TaxRegimes tr
                ON tr.Id = pbp.TaxRegimeId
               AND tr.CompanyId = @CompanyId
               AND tr.GcRecord = 0
            WHERE pbp.Id = @Id
              AND pbp.CompanyId = @CompanyId
              AND pbp.GcRecord = 0;
            """;

        var profile = await connection.QuerySingleOrDefaultAsync<PersonBusinessProfileResponseDto>(
            new CommandDefinition(
                sql,
                new
                {
                    request.Id,
                    CompanyId = currentUserService.CompanyId
                },
                cancellationToken: cancellationToken));

        if (profile is null)
        {
            return Response<PersonBusinessProfileResponseDto>.Error(
                "PERSON_BUSINESS_PROFILE_NOT_FOUND",
                ["Person business profile not found."]);
        }

        return new Response<PersonBusinessProfileResponseDto>
        {
            IsSuccess = true,
            Message = "Person business profile retrieved successfully.",
            Data = profile
        };
    }
}
