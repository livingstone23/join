using FluentAssertions;
using JOIN.Application.Services.Finance;
using JOIN.Application.UseCases.Finance.CompanyCurrencies.Commands;

namespace JOIN.Application.UnitTest.UseCases.Finance;

/// <summary>
/// SPEC 56 rules 1 and 6: exactly one live, active base currency per company, fixed once the company stores amounts.
/// </summary>
public sealed class CompanyCurrencyCommandsTests
{
    [Fact]
    public async Task Create_AsBase_WithoutAmounts_ShouldMoveTheBase()
    {
        using var ctx = await FinanceTestContext.CreateAsync();

        var response = await ctx.SendAsync(new CreateCompanyCurrencyCommand { CurrencyId = ctx.Currency["USD"], IsBase = true });

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Code.Should().Be("USD");
        response.Data.IsBase.Should().BeTrue();
        (await ctx.CompanyCurrenciesAsync()).Where(c => c.IsBase).Select(c => c.CurrencyId).Should().Equal(ctx.Currency["USD"]);
    }

    [Fact]
    public async Task Create_AsBase_WithAmounts_ShouldBeBlocked()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        ctx.AmountSource.HasAmounts = true;

        var response = await ctx.SendAsync(new CreateCompanyCurrencyCommand { CurrencyId = ctx.Currency["USD"], IsBase = true });

        response.Message.Should().Be(FinanceErrorCodes.BaseCurrencyInUse);
        response.Errors.Should().ContainSingle().Which.Should().Contain("FakeAmounts");
        (await ctx.CompanyCurrenciesAsync()).Single(c => c.IsBase).CurrencyId.Should().Be(ctx.Currency["DOP"]);
    }

    [Fact]
    public async Task Create_AsParallel_WithAmounts_ShouldWork()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        ctx.AmountSource.HasAmounts = true;

        var response = await ctx.SendAsync(new CreateCompanyCurrencyCommand { CurrencyId = ctx.Currency["USD"] });

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.IsBase.Should().BeFalse();
    }

    [Fact]
    public async Task Create_DuplicateUnknownOrInactiveBase_ShouldFail()
    {
        using var ctx = await FinanceTestContext.CreateAsync();

        (await ctx.SendAsync(new CreateCompanyCurrencyCommand { CurrencyId = ctx.Currency["DOP"] })).Message
            .Should().Be(FinanceErrorCodes.DuplicateKey);
        (await ctx.SendAsync(new CreateCompanyCurrencyCommand { CurrencyId = Guid.NewGuid() })).Message
            .Should().Be(FinanceErrorCodes.InvalidCurrency);
        (await ctx.SendAsync(new CreateCompanyCurrencyCommand { CurrencyId = ctx.Currency["USD"], IsBase = true, IsActive = false })).Message
            .Should().Be(FinanceErrorCodes.BaseCurrencyRequired);
    }

    [Fact]
    public async Task Create_WithInactiveGlobalCurrency_ShouldFail()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        var eur = await ctx.Db.Currencies.FindAsync(ctx.Currency["EUR"]);
        eur!.IsActive = false;
        await ctx.Db.SaveChangesAsync();

        (await ctx.SendAsync(new CreateCompanyCurrencyCommand { CurrencyId = ctx.Currency["EUR"] })).Message
            .Should().Be(FinanceErrorCodes.InvalidCurrency);
    }

    [Fact]
    public async Task Update_TheBase_CannotBeUnmarkedNorDeactivated()
    {
        using var ctx = await FinanceTestContext.CreateAsync();

        (await ctx.SendAsync(new UpdateCompanyCurrencyCommand { Id = ctx.DopCompanyCurrencyId, IsBase = false })).Message
            .Should().Be(FinanceErrorCodes.BaseCurrencyRequired);
        (await ctx.SendAsync(new UpdateCompanyCurrencyCommand { Id = ctx.DopCompanyCurrencyId, IsBase = true, IsActive = false })).Message
            .Should().Be(FinanceErrorCodes.BaseCurrencyRequired);
    }

    [Fact]
    public async Task Update_MarkingAParallelAsBase_ShouldMoveTheBaseOrBeBlocked()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        var usdId = await ctx.AddCompanyCurrencyAsync("USD");

        ctx.AmountSource.HasAmounts = true;
        (await ctx.SendAsync(new UpdateCompanyCurrencyCommand { Id = usdId, IsBase = true })).Message
            .Should().Be(FinanceErrorCodes.BaseCurrencyInUse);

        ctx.AmountSource.HasAmounts = false;
        var moved = await ctx.SendAsync(new UpdateCompanyCurrencyCommand { Id = usdId, IsBase = true });

        moved.IsSuccess.Should().BeTrue(moved.Message);
        var rows = await ctx.CompanyCurrenciesAsync();
        rows.Single(c => c.IsBase).Id.Should().Be(usdId);
        rows.Single(c => c.Id == ctx.DopCompanyCurrencyId).IsBase.Should().BeFalse();
    }

    [Fact]
    public async Task Update_AParallel_CanBeDeactivated_AndMissingRowsAreNotFound()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        var usdId = await ctx.AddCompanyCurrencyAsync("USD");

        var response = await ctx.SendAsync(new UpdateCompanyCurrencyCommand { Id = usdId, IsActive = false });

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.IsActive.Should().BeFalse();
        (await ctx.SendAsync(new UpdateCompanyCurrencyCommand { Id = Guid.NewGuid() })).Message
            .Should().Be(FinanceErrorCodes.CompanyCurrencyNotFound);
    }

    [Fact]
    public async Task Delete_TheBase_ShouldFail_AndAParallel_ShouldWork()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        var usdId = await ctx.AddCompanyCurrencyAsync("USD");

        (await ctx.SendAsync(new DeleteCompanyCurrencyCommand(ctx.DopCompanyCurrencyId))).Message
            .Should().Be(FinanceErrorCodes.BaseCurrencyRequired);

        var deleted = await ctx.SendAsync(new DeleteCompanyCurrencyCommand(usdId));

        deleted.IsSuccess.Should().BeTrue(deleted.Message);
        (await ctx.CompanyCurrenciesAsync()).Single(c => c.Id == usdId).IsDeleted.Should().BeTrue();
        (await ctx.SendAsync(new DeleteCompanyCurrencyCommand(usdId))).Message.Should().Be(FinanceErrorCodes.CompanyCurrencyNotFound);
    }

    [Fact]
    public async Task Restore_ShouldRequireSuperAdmin_AndComeBackAsParallelWhenABaseExists()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        var oldBaseId = await ctx.AddCompanyCurrencyAsync("EUR", isBase: false);
        var row = await ctx.Db.CompanyCurrencies.FindAsync(oldBaseId);
        row!.IsBase = true;
        row.MarkAsDeleted();
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(new RestoreCompanyCurrencyCommand(oldBaseId))).Message.Should().Be(FinanceErrorCodes.SuperAdminRequired);

        ctx.IsSuperAdmin = true;
        var restored = await ctx.SendAsync(new RestoreCompanyCurrencyCommand(oldBaseId));

        restored.IsSuccess.Should().BeTrue(restored.Message);
        var rows = await ctx.CompanyCurrenciesAsync();
        rows.Single(c => c.Id == oldBaseId).IsBase.Should().BeFalse("the company already has a live base");
        rows.Count(c => c.IsBase && !c.IsDeleted).Should().Be(1);
    }

    [Fact]
    public async Task Commands_WithoutCompany_ShouldReturnCompanyRequired()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        ctx.ActingCompanyId = Guid.Empty;

        (await ctx.SendAsync(new CreateCompanyCurrencyCommand { CurrencyId = ctx.Currency["USD"] })).Message.Should().Be(FinanceErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateCompanyCurrencyCommand { Id = ctx.DopCompanyCurrencyId })).Message.Should().Be(FinanceErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteCompanyCurrencyCommand(ctx.DopCompanyCurrencyId))).Message.Should().Be(FinanceErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new RestoreCompanyCurrencyCommand(ctx.DopCompanyCurrencyId))).Message.Should().Be(FinanceErrorCodes.CompanyRequired);
    }
}
