using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Workspaces;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Security.Workspaces.Queries.GetMyCompanies;

/// <summary>
/// Returns the companies assigned to the caller. Backs
/// <c>GET /api/v1/workspaces/my-companies</c>, consumed by the frontend's
/// "Empresas" tab and topbar switcher (specs/06-mi-cuenta-empresas.md).
/// Same SQL shape as <c>GetUserCompaniesQueryHandler</c> (the admin-facing
/// equivalent for another user's companies) — <c>Security.UserCompanies</c>
/// joined to <c>Common.Companies</c>, both non-soft-deleted.
/// </summary>
/// <param name="connectionFactory">Factory used to create engine-agnostic read connections.</param>
public sealed class GetMyCompaniesQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetMyCompaniesQuery, Response<IReadOnlyCollection<MyCompanyItemDto>>>
{
    private const string MyCompaniesSql = """
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

    /// <inheritdoc />
    public async Task<Response<IReadOnlyCollection<MyCompanyItemDto>>> Handle(
        GetMyCompaniesQuery request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            return Response<IReadOnlyCollection<MyCompanyItemDto>>.Error(
                "USER_NOT_FOUND",
                ["The authenticated user is not available."]);
        }

        using var connection = connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<MyCompanySqlRow>(
            new CommandDefinition(MyCompaniesSql, new { request.UserId }, cancellationToken: cancellationToken));

        var data = rows
            .Select(row => new MyCompanyItemDto
            {
                CompanyId = row.CompanyId,
                CompanyName = row.CompanyName ?? string.Empty,
                TaxId = row.TaxId ?? string.Empty,
                IsDefault = row.IsDefault
            })
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.CompanyName)
            .ToList();

        return new Response<IReadOnlyCollection<MyCompanyItemDto>>
        {
            IsSuccess = true,
            Message = "OK",
            Data = data
        };
    }

    private sealed class MyCompanySqlRow
    {
        public Guid CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public string? TaxId { get; set; }
        public bool IsDefault { get; set; }
    }
}
