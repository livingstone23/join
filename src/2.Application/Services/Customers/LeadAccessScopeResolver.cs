using Dapper;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Customers;
using JOIN.Domain.Customers.Teams;

namespace JOIN.Application.Services.Customers;

/// <inheritdoc />
public sealed class LeadAccessScopeResolver(ISqlConnectionFactory connectionFactory) : ILeadAccessScopeResolver
{
    private const string Sql = """
        SELECT
            m.SalesTeamId,
            r.LeadScope,
            r.CanReassign
        FROM customer.salesteammembers m
        INNER JOIN customer.teamroles r ON r.Id = m.TeamRoleId
        INNER JOIN customer.salesteams t ON t.Id = m.SalesTeamId AND t.GcRecord = 0
        WHERE m.CompanyId = @CompanyId
          AND m.UserId = @UserId
          AND m.GcRecord = 0
          AND m.ValidFrom <= @Today
          AND (m.ValidTo IS NULL OR m.ValidTo >= @Today);
        """;

    /// <inheritdoc />
    public async Task<LeadAccessScope> ResolveAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<Row>(new CommandDefinition(
                Sql,
                new { CompanyId = companyId, UserId = userId, Today = today.ToDateTime(TimeOnly.MinValue) },
                cancellationToken: cancellationToken)))
            .ToList();

        var companyWide = rows.Any(r => r.LeadScope == (int)LeadScope.Company);
        var teams = rows.Where(r => r.LeadScope == (int)LeadScope.Team).Select(r => r.SalesTeamId).Distinct().ToList();
        var reassignTeams = rows.Where(r => r.CanReassign).Select(r => r.SalesTeamId).Distinct().ToList();
        var reassignCompany = rows.Any(r => r.CanReassign && r.LeadScope == (int)LeadScope.Company);

        return new LeadAccessScope(userId, companyWide, teams, reassignTeams, reassignCompany);
    }

    private sealed class Row
    {
        public Guid SalesTeamId { get; set; }
        public int LeadScope { get; set; }
        public bool CanReassign { get; set; }
    }
}
