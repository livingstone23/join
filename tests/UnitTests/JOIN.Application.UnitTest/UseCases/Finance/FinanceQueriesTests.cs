using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Finance;
using JOIN.Application.Services.Finance;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Finance.CompanyCurrencies.Queries;
using JOIN.Application.UseCases.Finance.Currencies.Queries;
using JOIN.Application.UseCases.Finance.ExchangeRates.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Finance;

/// <summary>
/// SPEC 56 query handlers: tenant scope, filters and the rate resolution endpoint.
/// </summary>
public sealed class FinanceQueriesTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly IOptions<PaginationSettings> _paging = Options.Create(new PaginationSettings());

    public FinanceQueriesTests()
    {
        _user.SetupGet(x => x.CompanyId).Returns(_companyId);
        _user.Setup(x => x.IsInRole("SuperAdmin")).Returns(false);
    }

    [Fact]
    public async Task GetCurrencies_ShouldPageAndFilter()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["GcRecord"] = 0, ["Code"] = "USD", ["Name"] = "Dollar", ["Symbol"] = "$", ["Decimals"] = 2, ["IsActive"] = true, ["CreatedAt"] = DateTime.UtcNow, ["CompaniesCount"] = 3 }),
            FakeResultSet.FromScalar(1));

        var response = await new GetCurrenciesQueryHandler(factory, _paging, _user.Object)
            .Handle(new GetCurrenciesQuery("us", true, null, 1, 10), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.TotalCount.Should().Be(1);
        response.Data.Items.Single().CompaniesCount.Should().Be(3);
        factory.LastCommandText.Should().Contain("c.Code ILIKE @Search").And.Contain("c.IsActive = @IsActive").And.Contain("c.GcRecord = 0");
    }

    [Fact]
    public async Task GetCurrencyById_ShouldReturnNotFoundWhenMissing()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id", "Code"));

        var response = await new GetCurrencyByIdQueryHandler(factory, _user.Object).Handle(new GetCurrencyByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be(FinanceErrorCodes.CurrencyNotFound);
    }

    [Fact]
    public async Task GetCompanyCurrencies_ShouldScopeToTheTokenCompany_AndOrderTheBaseFirst()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["GcRecord"] = 0, ["CompanyId"] = _companyId, ["CurrencyId"] = Guid.NewGuid(), ["Code"] = "DOP", ["Name"] = "Peso", ["Symbol"] = "RD$", ["Decimals"] = 2, ["IsBase"] = true, ["IsActive"] = true, ["LatestRate"] = null, ["LatestRateDate"] = null, ["CreatedAt"] = DateTime.UtcNow }));

        var response = await new GetCompanyCurrenciesQueryHandler(factory, _user.Object)
            .Handle(new GetCompanyCurrenciesQuery(IncludeInactive: false, CompanyId: Guid.NewGuid()), CancellationToken.None);

        response.Data!.Single().IsBase.Should().BeTrue();
        factory.CapturedParameters["CompanyId"].Should().Be(_companyId, "a non-SuperAdmin cannot point at another company");
        factory.LastCommandText.Should().Contain("ORDER BY cc.IsBase DESC").And.Contain("cc.IsActive = TRUE").And.Contain("LEFT JOIN LATERAL");
    }

    [Fact]
    public async Task GetCompanyCurrencyById_And_GetExchangeRateById_ShouldReturnNotFoundWhenMissing()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"));

        (await new GetCompanyCurrencyByIdQueryHandler(factory, _user.Object).Handle(new GetCompanyCurrencyByIdQuery(Guid.NewGuid()), CancellationToken.None))
            .Message.Should().Be(FinanceErrorCodes.CompanyCurrencyNotFound);
        (await new GetExchangeRateByIdQueryHandler(factory, _user.Object).Handle(new GetExchangeRateByIdQuery(Guid.NewGuid()), CancellationToken.None))
            .Message.Should().Be(FinanceErrorCodes.ExchangeRateRecordNotFound);
    }

    [Fact]
    public async Task GetExchangeRates_ShouldApplyTheFilters()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id", "CurrencyCode"), FakeResultSet.FromScalar(0));
        var currencyId = Guid.NewGuid();

        var response = await new GetExchangeRatesQueryHandler(factory, _paging, _user.Object)
            .Handle(new GetExchangeRatesQuery(currencyId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        factory.CapturedParameters["CurrencyId"].Should().Be(currencyId);
        factory.LastCommandText.Should().Contain("r.RateDate >= @From").And.Contain("r.RateDate <= @To").And.Contain("r.GcRecord = 0");
    }

    [Fact]
    public async Task Resolve_ShouldMapTheResolution_OrReturnExchangeRateNotFound()
    {
        var resolver = new Mock<IExchangeRateResolver>();
        var currencyId = Guid.NewGuid();
        resolver.Setup(r => r.ResolveAsync(_companyId, currencyId, new DateOnly(2026, 10, 16), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeRateResolution(59.36m, new DateOnly(2026, 10, 15), false));
        var handler = new ResolveExchangeRateQueryHandler(resolver.Object, _user.Object);

        var found = await handler.Handle(new ResolveExchangeRateQuery(currencyId, new DateOnly(2026, 10, 16)), CancellationToken.None);
        found.Data!.Rate.Should().Be(59.36m);
        found.Data.RateDate.Should().Be(new DateOnly(2026, 10, 15));

        var missing = await handler.Handle(new ResolveExchangeRateQuery(Guid.NewGuid(), new DateOnly(2026, 10, 16)), CancellationToken.None);
        missing.Message.Should().Be(FinanceErrorCodes.ExchangeRateNotFound);
    }

    [Fact]
    public async Task TenantQueries_WithoutCompany_ShouldReturnCompanyRequired()
    {
        _user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        var factory = new FakeSqlConnectionFactory();

        (await new GetCompanyCurrenciesQueryHandler(factory, _user.Object).Handle(new GetCompanyCurrenciesQuery(), CancellationToken.None)).Message.Should().Be(FinanceErrorCodes.CompanyRequired);
        (await new GetCompanyCurrencyByIdQueryHandler(factory, _user.Object).Handle(new GetCompanyCurrencyByIdQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(FinanceErrorCodes.CompanyRequired);
        (await new GetExchangeRatesQueryHandler(factory, _paging, _user.Object).Handle(new GetExchangeRatesQuery(), CancellationToken.None)).Message.Should().Be(FinanceErrorCodes.CompanyRequired);
        (await new GetExchangeRateByIdQueryHandler(factory, _user.Object).Handle(new GetExchangeRateByIdQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(FinanceErrorCodes.CompanyRequired);
        (await new ResolveExchangeRateQueryHandler(new Mock<IExchangeRateResolver>().Object, _user.Object).Handle(new ResolveExchangeRateQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(FinanceErrorCodes.CompanyRequired);
    }
}
