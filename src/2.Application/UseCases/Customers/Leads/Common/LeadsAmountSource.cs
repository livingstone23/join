using Dapper;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Finance;

namespace JOIN.Application.UseCases.Customers.Leads.Common;

/// <summary>
/// SPEC 61 B.13: a company "stores amounts" in its base currency as soon as a live lead has an estimated value, so
/// SPEC 56 locks the base currency from then on.
/// </summary>
public sealed class LeadsAmountSource(ISqlConnectionFactory connectionFactory) : IBaseCurrencyAmountSource
{
    private const string Sql = """
        SELECT EXISTS (
            SELECT 1
            FROM customer.leads l
            WHERE l.CompanyId = @CompanyId
              AND l.GcRecord = 0
              AND l.EstimatedValue > 0);
        """;

    /// <inheritdoc />
    public string Name => "Leads";

    /// <inheritdoc />
    public async Task<bool> HasAmountsAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(Sql, new { CompanyId = companyId }, cancellationToken: cancellationToken));
    }
}
