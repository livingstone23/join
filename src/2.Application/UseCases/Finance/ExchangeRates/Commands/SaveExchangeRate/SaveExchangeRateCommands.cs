using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;

namespace JOIN.Application.UseCases.Finance.ExchangeRates.Commands;

/// <summary>
/// Fields shared by the create and update commands of an exchange rate.
/// </summary>
public interface IExchangeRateInput
{
    Guid CurrencyId { get; }
    DateOnly RateDate { get; }
    decimal Rate { get; }
}

/// <summary>
/// <c>POST /ExchangeRates</c> (SPEC 56): 1 unit of the currency = <c>Rate</c> units of the company's base currency
/// on <c>RateDate</c>. Only for active parallel currencies of the company of the token.
/// </summary>
public sealed record CreateExchangeRateCommand : IExchangeRateInput, ITransactionalCommand<Response<ExchangeRateDto>>
{
    public Guid CurrencyId { get; init; }
    public DateOnly RateDate { get; init; }
    public decimal Rate { get; init; }
}

/// <summary>
/// <c>PUT /ExchangeRates/{id}</c> (SPEC 56).
/// </summary>
public sealed record UpdateExchangeRateCommand : IExchangeRateInput, ITransactionalCommand<Response<ExchangeRateDto>>
{
    public Guid Id { get; init; }
    public Guid CurrencyId { get; init; }
    public DateOnly RateDate { get; init; }
    public decimal Rate { get; init; }
}

/// <summary>
/// <c>DELETE /ExchangeRates/{id}</c>: logical delete.
/// </summary>
public sealed record DeleteExchangeRateCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary>
/// <c>POST /ExchangeRates/{id}/restore</c> (SPEC 41-post, SuperAdmin).
/// </summary>
public sealed record RestoreExchangeRateCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
