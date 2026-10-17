using FluentAssertions;
using JOIN.Application.Services.Finance;
using JOIN.Application.UseCases.Finance.Currencies.Commands;

namespace JOIN.Application.UnitTest.UseCases.Finance;

/// <summary>
/// SPEC 56 rule 7: the global ISO catalog (SuperAdmin endpoint).
/// </summary>
public sealed class CurrencyCommandsTests
{
    [Fact]
    public async Task Create_ShouldNormalizeTheCode_AndRejectDuplicates()
    {
        using var ctx = await FinanceTestContext.CreateAsync();

        var created = await ctx.SendAsync(new CreateCurrencyCommand { Code = " cop ", Name = " Peso colombiano ", Symbol = "COL$" });

        created.IsSuccess.Should().BeTrue(created.Message);
        created.Data!.Code.Should().Be("COP");
        created.Data.Name.Should().Be("Peso colombiano");
        (await ctx.SendAsync(new CreateCurrencyCommand { Code = "usd", Name = "x", Symbol = "$" })).Message.Should().Be(FinanceErrorCodes.DuplicateKey);
    }

    [Fact]
    public async Task Update_ShouldChangeTheFields_AndRejectDuplicatesOrMissing()
    {
        using var ctx = await FinanceTestContext.CreateAsync();

        var updated = await ctx.SendAsync(new UpdateCurrencyCommand { Id = ctx.Currency["EUR"], Code = "EUR", Name = "Euro", Symbol = "€", Decimals = 2 });

        updated.IsSuccess.Should().BeTrue(updated.Message);
        updated.Data!.Symbol.Should().Be("€");
        (await ctx.SendAsync(new UpdateCurrencyCommand { Id = ctx.Currency["EUR"], Code = "USD", Name = "x", Symbol = "$" })).Message.Should().Be(FinanceErrorCodes.DuplicateKey);
        (await ctx.SendAsync(new UpdateCurrencyCommand { Id = Guid.NewGuid(), Code = "XXX", Name = "x", Symbol = "$" })).Message.Should().Be(FinanceErrorCodes.CurrencyNotFound);
    }

    [Fact]
    public async Task Delete_ACurrencyInUse_ShouldFail_AndAnUnusedOne_ShouldWorkAndRestore()
    {
        using var ctx = await FinanceTestContext.CreateAsync();

        var inUse = await ctx.SendAsync(new DeleteCurrencyCommand(ctx.Currency["DOP"]));
        inUse.Message.Should().Be(FinanceErrorCodes.CurrencyInUse);
        inUse.Errors.Should().Equal("Active CompanyCurrencies: 1");

        (await ctx.SendAsync(new DeleteCurrencyCommand(ctx.Currency["EUR"]))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new DeleteCurrencyCommand(ctx.Currency["EUR"]))).Message.Should().Be(FinanceErrorCodes.CurrencyNotFound);

        var restored = await ctx.SendAsync(new RestoreCurrencyCommand(ctx.Currency["EUR"]));
        restored.IsSuccess.Should().BeTrue(restored.Message);
    }
}
