using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Queries;

/// <summary>
/// Handles person financial profile detail queries using Dapper with catalog joins.
/// </summary>
/// <param name="connectionFactory">Factory used to create read connections.</param>
/// <param name="currentUserService">Service that exposes the active tenant identifier.</param>
public sealed class GetPersonFinancialProfileByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonFinancialProfileByIdQuery, Response<PersonFinancialProfileResponseDto>>
{
    /// <summary>
    /// Retrieves a single financial profile restricted to the current tenant and active rows only.
    /// </summary>
    /// <param name="request">The query containing the financial profile identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A response containing the requested financial profile data.</returns>
    public async Task<Response<PersonFinancialProfileResponseDto>> Handle(
        GetPersonFinancialProfileByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PersonFinancialProfileResponseDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                pfp.Id,
                pfp.PersonId,
                pfp.IncomeRangeId,
                ir.DisplayName AS IncomeRangeDisplayName,
                pfp.SourceOfFunds,
                pfp.DeclaredDate,
                pfp.IsCurrent,
                pfp.IsActive
            FROM Admin.PersonFinancialProfiles pfp
            INNER JOIN Admin.IncomeRanges ir
                ON ir.Id = pfp.IncomeRangeId
               AND ir.CompanyId = @CompanyId
               AND ir.GcRecord = 0
            WHERE pfp.Id = @Id
              AND pfp.CompanyId = @CompanyId
              AND pfp.GcRecord = 0;
            """;

        var profile = await connection.QuerySingleOrDefaultAsync<PersonFinancialProfileResponseDto>(
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
            return Response<PersonFinancialProfileResponseDto>.Error(
                "PERSON_FINANCIAL_PROFILE_NOT_FOUND",
                ["Person financial profile not found."]);
        }

        return new Response<PersonFinancialProfileResponseDto>
        {
            IsSuccess = true,
            Message = "Person financial profile retrieved successfully.",
            Data = profile
        };
    }
}
