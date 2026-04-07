using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Security.UserCompanies.Queries.GetUserCompanies;

/// <summary>
/// Handles the retrieval of the companies linked to a user.
/// </summary>
/// <param name="connectionFactory">Factory used to create engine-agnostic read connections.</param>
public sealed class GetUserCompaniesQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetUserCompaniesQuery, Response<IEnumerable<UserCompanyDto>>>
{
    private const string UserExistsSql = """
        SELECT u.Id
        FROM Security.Users u
        WHERE u.Id = @UserId
          AND u.GcRecord = 0;
        """;

    private const string UserCompaniesSql = """
        SELECT
            c.Id AS CompanyId,
            c.Name AS CompanyName,
            c.TaxId,
            uc.IsDefault
        FROM Security.UserCompanies uc
        INNER JOIN Common.Companies c
            ON c.Id = uc.CompanyId
        WHERE uc.UserId = @UserId
          AND uc.GcRecord = 0
          AND c.GcRecord = 0
        ORDER BY c.Name;
        """;

    /// <summary>
    /// Retrieves the active company assignments for the requested user.
    /// </summary>
    /// <param name="request">The query payload.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A standardized response containing the linked companies.</returns>
    public async Task<Response<IEnumerable<UserCompanyDto>>> Handle(
        GetUserCompaniesQuery request,
        CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        var userId = await connection.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(UserExistsSql, new { request.UserId }, cancellationToken: cancellationToken));

        if (!userId.HasValue || userId == Guid.Empty)
        {
            return new Response<IEnumerable<UserCompanyDto>>
            {
                IsSuccess = false,
                Message = "User not found."
            };
        }

        var companies = (await connection.QueryAsync<UserCompanySqlRow>(
            new CommandDefinition(UserCompaniesSql, new { request.UserId }, cancellationToken: cancellationToken)))
            .Select(item => new UserCompanyDto
            {
                CompanyId = item.CompanyId,
                CompanyName = item.CompanyName ?? string.Empty,
                TaxId = item.TaxId ?? string.Empty,
                IsDefault = item.IsDefault
            })
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.CompanyName)
            .ToArray();

        return new Response<IEnumerable<UserCompanyDto>>
        {
            Data = companies,
            IsSuccess = true,
            Message = "User companies retrieved successfully."
        };
    }

    private sealed class UserCompanySqlRow
    {
        public Guid CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public string? TaxId { get; set; }
        public bool IsDefault { get; set; }
    }
}
