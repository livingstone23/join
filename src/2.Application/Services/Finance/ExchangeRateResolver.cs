using Dapper;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Finance;

namespace JOIN.Application.Services.Finance;

/// <summary>
/// SPEC 56 rule 4: the base currency is 1; a parallel currency uses the rate of the date or the latest earlier one.
/// Read-only, with Dapper.
/// </summary>
public sealed class ExchangeRateResolver(ISqlConnectionFactory connectionFactory) : IExchangeRateResolver
{
    private const string CurrencySql = """
        SELECT cc.IsBase
        FROM finance.companycurrencies cc
        WHERE cc.CompanyId = @CompanyId
          AND cc.CurrencyId = @CurrencyId
          AND cc.GcRecord = 0
          AND cc.IsActive = TRUE;
        """;

    private const string RateSql = """
        SELECT r.Rate, r.RateDate
        FROM finance.exchangerates r
        WHERE r.CompanyId = @CompanyId
          AND r.CurrencyId = @CurrencyId
          AND r.GcRecord = 0
          AND r.RateDate <= @Date
        ORDER BY r.RateDate DESC
        LIMIT 1;
        """;

    /// <inheritdoc />
    public async Task<ExchangeRateResolution?> ResolveAsync(Guid companyId, Guid currencyId, DateOnly date, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var isBase = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            CurrencySql,
            new { CompanyId = companyId, CurrencyId = currencyId },
            cancellationToken: cancellationToken));

        if (isBase is null)
        {
            return null;
        }

        if (isBase.Value)
        {
            return new ExchangeRateResolution(1m, null, true);
        }

        var rate = await connection.QuerySingleOrDefaultAsync<RateRow>(new CommandDefinition(
            RateSql,
            // Dapper does not bind DateOnly parameters: send the date as a DateTime, as the calendar queries do.
            new { CompanyId = companyId, CurrencyId = currencyId, Date = date.ToDateTime(TimeOnly.MinValue) },
            cancellationToken: cancellationToken));

        return rate is null ? null : new ExchangeRateResolution(rate.Rate, rate.RateDate, false);
    }

    private sealed class RateRow
    {
        public decimal Rate { get; init; }
        public DateOnly RateDate { get; init; }
    }
}
