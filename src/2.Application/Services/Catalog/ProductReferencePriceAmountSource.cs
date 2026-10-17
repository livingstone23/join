using Dapper;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Finance;

namespace JOIN.Application.Services.Catalog;

/// <summary>
/// SPEC 57 C.7: a company "stores amounts" in its base currency as soon as a live product has a reference price, so
/// SPEC 56 locks the base currency from then on.
/// </summary>
public sealed class ProductReferencePriceAmountSource(ISqlConnectionFactory connectionFactory) : IBaseCurrencyAmountSource
{
    private const string Sql = """
        SELECT EXISTS (
            SELECT 1
            FROM catalog.products p
            WHERE p.CompanyId = @CompanyId
              AND p.GcRecord = 0
              AND p.ReferencePrice > 0);
        """;

    /// <inheritdoc />
    public string Name => "Products";

    /// <inheritdoc />
    public async Task<bool> HasAmountsAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(Sql, new { CompanyId = companyId }, cancellationToken: cancellationToken));
    }
}
