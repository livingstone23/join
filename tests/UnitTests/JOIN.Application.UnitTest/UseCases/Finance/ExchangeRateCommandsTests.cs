using FluentAssertions;
using JOIN.Application.Services.Finance;
using JOIN.Application.UseCases.Finance.ExchangeRates.Commands;

namespace JOIN.Application.UnitTest.UseCases.Finance;

/// <summary>
/// SPEC 56: rates only for active parallel currencies of the company, one per currency and date.
/// </summary>
public sealed class ExchangeRateCommandsTests
{
    private static readonly DateOnly Day = new(2026, 10, 16);

    [Fact]
    public async Task Create_ForAParallelCurrency_ShouldWork_AndCarryTheBaseCode()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        await ctx.AddCompanyCurrencyAsync("USD");

        var response = await ctx.SendAsync(new CreateExchangeRateCommand { CurrencyId = ctx.Currency["USD"], RateDate = Day, Rate = 59.36m });

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.CurrencyCode.Should().Be("USD");
        response.Data.BaseCurrencyCode.Should().Be("DOP");
        response.Data.Rate.Should().Be(59.36m);
    }

    [Fact]
    public async Task Create_ForTheBaseOrAForeignOrInactiveCurrency_ShouldFail()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        await ctx.AddCompanyCurrencyAsync("USD", isActive: false);

        (await ctx.SendAsync(new CreateExchangeRateCommand { CurrencyId = ctx.Currency["DOP"], RateDate = Day, Rate = 1m })).Message
            .Should().Be(FinanceErrorCodes.ExchangeRateForBase);
        (await ctx.SendAsync(new CreateExchangeRateCommand { CurrencyId = ctx.Currency["EUR"], RateDate = Day, Rate = 64m })).Message
            .Should().Be(FinanceErrorCodes.ExchangeRateInvalid, "EUR is not a currency of the company");
        (await ctx.SendAsync(new CreateExchangeRateCommand { CurrencyId = ctx.Currency["USD"], RateDate = Day, Rate = 59m })).Message
            .Should().Be(FinanceErrorCodes.ExchangeRateInvalid, "USD is inactive in the company");
    }

    [Fact]
    public async Task CreateAndUpdate_OnATakenDate_ShouldFail()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        await ctx.AddCompanyCurrencyAsync("USD");
        await ctx.SendAsync(new CreateExchangeRateCommand { CurrencyId = ctx.Currency["USD"], RateDate = Day, Rate = 59.36m });
        var other = await ctx.SendAsync(new CreateExchangeRateCommand { CurrencyId = ctx.Currency["USD"], RateDate = Day.AddDays(1), Rate = 59.40m });

        (await ctx.SendAsync(new CreateExchangeRateCommand { CurrencyId = ctx.Currency["USD"], RateDate = Day, Rate = 60m })).Message
            .Should().Be(FinanceErrorCodes.DuplicateKey);
        (await ctx.SendAsync(new UpdateExchangeRateCommand { Id = other.Data!.Id, CurrencyId = ctx.Currency["USD"], RateDate = Day, Rate = 60m })).Message
            .Should().Be(FinanceErrorCodes.DuplicateKey);

        var updated = await ctx.SendAsync(new UpdateExchangeRateCommand { Id = other.Data.Id, CurrencyId = ctx.Currency["USD"], RateDate = Day.AddDays(1), Rate = 59.5m });
        updated.IsSuccess.Should().BeTrue(updated.Message);
        updated.Data!.Rate.Should().Be(59.5m);
    }

    [Fact]
    public async Task UpdateDeleteAndRestore_ShouldFollowTheRules()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        await ctx.AddCompanyCurrencyAsync("USD");
        var created = await ctx.SendAsync(new CreateExchangeRateCommand { CurrencyId = ctx.Currency["USD"], RateDate = Day, Rate = 59.36m });

        (await ctx.SendAsync(new UpdateExchangeRateCommand { Id = Guid.NewGuid(), CurrencyId = ctx.Currency["USD"], RateDate = Day, Rate = 1m })).Message
            .Should().Be(FinanceErrorCodes.ExchangeRateRecordNotFound);

        (await ctx.SendAsync(new DeleteExchangeRateCommand(created.Data!.Id))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new DeleteExchangeRateCommand(created.Data.Id))).Message.Should().Be(FinanceErrorCodes.ExchangeRateRecordNotFound);

        // A new rate takes the date; restoring the old one would duplicate it.
        await ctx.SendAsync(new CreateExchangeRateCommand { CurrencyId = ctx.Currency["USD"], RateDate = Day, Rate = 59.50m });
        (await ctx.SendAsync(new RestoreExchangeRateCommand(created.Data.Id))).Message.Should().Be(FinanceErrorCodes.SuperAdminRequired);
        ctx.IsSuperAdmin = true;
        (await ctx.SendAsync(new RestoreExchangeRateCommand(created.Data.Id))).Message.Should().Be("ACTIVE_DUPLICATE_EXISTS");
    }

    [Fact]
    public async Task Commands_WithoutCompany_ShouldReturnCompanyRequired()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        ctx.ActingCompanyId = Guid.Empty;

        (await ctx.SendAsync(new CreateExchangeRateCommand { CurrencyId = ctx.Currency["USD"], RateDate = Day, Rate = 1m })).Message.Should().Be(FinanceErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateExchangeRateCommand { Id = Guid.NewGuid(), CurrencyId = ctx.Currency["USD"], RateDate = Day, Rate = 1m })).Message.Should().Be(FinanceErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteExchangeRateCommand(Guid.NewGuid()))).Message.Should().Be(FinanceErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new RestoreExchangeRateCommand(Guid.NewGuid()))).Message.Should().Be(FinanceErrorCodes.CompanyRequired);
    }
}
