using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Queries;

/// <summary>
/// Handles person financial profile listing requests using Dapper with catalog joins.
/// </summary>
public sealed class GetPersonFinancialProfilesByPersonIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonFinancialProfilesByPersonIdQuery, Response<List<PersonFinancialProfileResponseDto>>>
{
    /// <summary>
    /// Retrieves person financial profiles from SQL using tenant and soft-delete filters.
    /// </summary>
    /// <param name="request">The request carrying the person identifier.</param>
    /// <param name="cancellationToken">A cancellation token for query execution.</param>
    /// <returns>A response containing the list of person financial profiles.</returns>
    public async Task<Response<List<PersonFinancialProfileResponseDto>>> Handle(
        GetPersonFinancialProfilesByPersonIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<List<PersonFinancialProfileResponseDto>>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                pfp.Id,
                pfp.PersonId,
                pfp.IncomeRangeId,
                ir.DisplayName AS IncomeRangeName,
                pfp.SourceOfFunds,
                pfp.DeclaredDate,
                pfp.IsCurrent,
                pfp.IsActive
            FROM Admin.PersonFinancialProfiles pfp
            INNER JOIN Admin.IncomeRanges ir
                ON ir.Id = pfp.IncomeRangeId
               AND ir.CompanyId = @CompanyId
               AND ir.GcRecord = 0
            WHERE pfp.PersonId = @PersonId
              AND pfp.CompanyId = @CompanyId
              AND pfp.GcRecord = 0
            ORDER BY pfp.IsCurrent DESC, pfp.DeclaredDate DESC;
            """;

        var rows = await connection.QueryAsync<PersonFinancialProfileResponseDto>(
            new CommandDefinition(
                sql,
                new
                {
                    request.PersonId,
                    CompanyId = currentUserService.CompanyId
                },
                cancellationToken: cancellationToken));

        return new Response<List<PersonFinancialProfileResponseDto>>
        {
            IsSuccess = true,
            Message = "Person financial profiles retrieved successfully.",
            Data = rows.ToList()
        };
    }
}
