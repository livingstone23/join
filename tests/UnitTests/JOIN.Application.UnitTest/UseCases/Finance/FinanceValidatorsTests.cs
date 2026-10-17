using FluentAssertions;
using JOIN.Application.UseCases.Finance.CompanyCurrencies.Commands;
using JOIN.Application.UseCases.Finance.Currencies.Commands;
using JOIN.Application.UseCases.Finance.ExchangeRates.Commands;

namespace JOIN.Application.UnitTest.UseCases.Finance;

/// <summary>
/// SPEC 56 validators: ISO code, decimals, positive rate with up to 6 decimals, required ids.
/// </summary>
public sealed class FinanceValidatorsTests
{
    [Theory]
    [InlineData("USD", 2, true)]
    [InlineData("US", 2, false)]
    [InlineData("U5D", 2, false)]
    [InlineData("USD", 5, false)]
    public void CurrencyValidator_ShouldCheckTheCodeAndDecimals(string code, int decimals, bool valid)
    {
        var command = new CreateCurrencyCommand { Code = code, Name = "Name", Symbol = "$", Decimals = decimals };

        new CreateCurrencyCommandValidator().Validate(command).IsValid.Should().Be(valid);
        new UpdateCurrencyCommandValidator().Validate(new UpdateCurrencyCommand { Id = Guid.NewGuid(), Code = code, Name = "Name", Symbol = "$", Decimals = decimals })
            .IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("59.36", true)]
    [InlineData("0.014423", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("1.1234567", false)]
    public void ExchangeRateValidator_ShouldRequireAPositiveRateWithSixDecimals(string rate, bool valid)
    {
        var command = new CreateExchangeRateCommand { CurrencyId = Guid.NewGuid(), RateDate = new DateOnly(2026, 10, 16), Rate = decimal.Parse(rate) };

        new CreateExchangeRateCommandValidator().Validate(command).IsValid.Should().Be(valid);
        new UpdateExchangeRateCommandValidator().Validate(new UpdateExchangeRateCommand { Id = Guid.NewGuid(), CurrencyId = command.CurrencyId, RateDate = command.RateDate, Rate = command.Rate })
            .IsValid.Should().Be(valid);
    }

    [Fact]
    public void Validators_ShouldRequireTheIds()
    {
        new CreateCompanyCurrencyCommandValidator().Validate(new CreateCompanyCurrencyCommand()).IsValid.Should().BeFalse();
        new UpdateCompanyCurrencyCommandValidator().Validate(new UpdateCompanyCurrencyCommand()).IsValid.Should().BeFalse();
        new UpdateCurrencyCommandValidator().Validate(new UpdateCurrencyCommand { Code = "USD", Name = "x", Symbol = "$" }).IsValid.Should().BeFalse();
        new CreateExchangeRateCommandValidator().Validate(new CreateExchangeRateCommand { RateDate = new DateOnly(2026, 1, 1), Rate = 1 }).IsValid.Should().BeFalse();
    }
}
