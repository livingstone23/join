using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Queries;

/// <summary>
/// Handles person business profile listing requests using Dapper with catalog joins.
/// </summary>
public sealed class GetPersonBusinessProfilesByPersonIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonBusinessProfilesByPersonIdQuery, Response<List<PersonBusinessProfileResponseDto>>>
{
    /// <summary>
    /// Retrieves person business profiles from SQL using tenant and soft-delete filters.
    /// </summary>
    /// <param name="request">The request carrying the person identifier.</param>
    /// <param name="cancellationToken">A cancellation token for query execution.</param>
    /// <returns>A response containing the list of person business profiles.</returns>
    public async Task<Response<List<PersonBusinessProfileResponseDto>>> Handle(
        GetPersonBusinessProfilesByPersonIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<List<PersonBusinessProfileResponseDto>>.Error(
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
                pbp.IsActive,
                pbp.Created AS CreatedAt
            FROM Admin.PersonBusinessProfiles pbp
            INNER JOIN Admin.Industries i
                ON i.Id = pbp.IndustryId
               AND i.CompanyId = @CompanyId
               AND i.GcRecord = 0
            INNER JOIN Admin.TaxRegimes tr
                ON tr.Id = pbp.TaxRegimeId
               AND tr.CompanyId = @CompanyId
               AND tr.GcRecord = 0
            WHERE pbp.PersonId = @PersonId
              AND pbp.CompanyId = @CompanyId
              AND pbp.GcRecord = 0
            ORDER BY pbp.IsActive DESC, pbp.Created DESC;
            """;

        var rows = await connection.QueryAsync<PersonBusinessProfileResponseDto>(
            new CommandDefinition(
                sql,
                new
                {
                    request.PersonId,
                    CompanyId = currentUserService.CompanyId
                },
                cancellationToken: cancellationToken));

        return new Response<List<PersonBusinessProfileResponseDto>>
        {
            IsSuccess = true,
            Message = "Person business profiles retrieved successfully.",
            Data = rows.ToList()
        };
    }
}
