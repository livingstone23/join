using FluentAssertions;
using JOIN.Application.Interface.Finance;
using JOIN.Application.Services.Finance;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Finance.CompanyCurrencies;

namespace JOIN.Application.UnitTest.UseCases.Finance;

/// <summary>
/// SPEC 56 rules 3, 4 and 6: conversion rounding, rate resolution with fallback, and the base-change guard.
/// </summary>
public sealed class FinanceServicesTests
{
    [Theory]
    [InlineData("12500", "59.36", "742000.00")]
    [InlineData("1.005", "1", "1.01")]
    [InlineData("-1.005", "1", "-1.01")]
    [InlineData("100", "0.014423", "1.44")]
    public void MoneyConverter_ShouldRoundToTwoDecimalsAwayFromZero(string amount, string rate, string expected)
    {
        new MoneyConverter().ToBase(decimal.Parse(amount), decimal.Parse(rate)).Should().Be(decimal.Parse(expected));
    }

    [Fact]
    public async Task Resolver_ForTheBase_ShouldReturnOne()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.FromScalar(true));

        var result = await new ExchangeRateResolver(factory).ResolveAsync(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 16));

        result.Should().Be(new ExchangeRateResolution(1m, null, true));
    }

    [Fact]
    public async Task Resolver_ForAParallelCurrency_ShouldReturnTheRateOfTheDateOrTheLatestEarlier()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(
            FakeResultSet.FromScalar(false),
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["Rate"] = 59.36m, ["RateDate"] = new DateOnly(2026, 10, 14) }));
        var date = new DateOnly(2026, 10, 16);

        var result = await new ExchangeRateResolver(factory).ResolveAsync(Guid.NewGuid(), Guid.NewGuid(), date);

        result.Should().Be(new ExchangeRateResolution(59.36m, new DateOnly(2026, 10, 14), false));
        factory.CapturedParameters["Date"].Should().Be(date.ToDateTime(TimeOnly.MinValue));
        factory.LastCommandText.Should().Contain("r.RateDate <= @Date").And.Contain("ORDER BY r.RateDate DESC");
    }

    [Fact]
    public async Task Resolver_WithoutTheCurrencyOrWithoutARate_ShouldReturnNull()
    {
        var notInCompany = new FakeSqlConnectionFactory();
        notInCompany.SetResults(FakeResultSet.Empty("IsBase"));
        (await new ExchangeRateResolver(notInCompany).ResolveAsync(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 16))).Should().BeNull();

        var noRate = new FakeSqlConnectionFactory();
        noRate.SetResults(FakeResultSet.FromScalar(false), FakeResultSet.Empty("Rate", "RateDate"));
        (await new ExchangeRateResolver(noRate).ResolveAsync(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 16))).Should().BeNull();
    }

    [Fact]
    public async Task Guard_ShouldListOnlyTheSourcesWithAmounts()
    {
        var guard = new CompanyBaseCurrencyChangeGuard([new Source("Products", true), new Source("Leads", false), new Source("Quotes", true)]);

        (await guard.GetSourcesWithAmountsAsync(Guid.NewGuid(), CancellationToken.None)).Should().Equal("Products", "Quotes");
        (await new CompanyBaseCurrencyChangeGuard([]).GetSourcesWithAmountsAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeEmpty();
    }

    private sealed class Source(string name, bool hasAmounts) : IBaseCurrencyAmountSource
    {
        public string Name => name;
        public Task<bool> HasAmountsAsync(Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(hasAmounts);
    }
}
