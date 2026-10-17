using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;

namespace JOIN.Application.UseCases.Finance.Currencies.Commands;

/// <summary>
/// Fields shared by the create and update commands of a global currency.
/// </summary>
public interface ICurrencyInput
{
    string Code { get; }
    string Name { get; }
    string Symbol { get; }
    int Decimals { get; }
    bool IsActive { get; }
}

/// <summary>
/// <c>POST /Currencies</c> (SPEC 56, SuperAdmin): adds an ISO currency to the global catalog.
/// </summary>
public sealed record CreateCurrencyCommand : ICurrencyInput, ITransactionalCommand<Response<CurrencyDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public int Decimals { get; init; } = 2;
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// <c>PUT /Currencies/{id}</c> (SPEC 56, SuperAdmin).
/// </summary>
public sealed record UpdateCurrencyCommand : ICurrencyInput, ITransactionalCommand<Response<CurrencyDto>>
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public int Decimals { get; init; } = 2;
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// <c>DELETE /Currencies/{id}</c>: logical delete, blocked while a company uses it (<c>CURRENCY_IN_USE</c>).
/// </summary>
public sealed record DeleteCurrencyCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary>
/// <c>POST /Currencies/{id}/restore</c> (SPEC 41-post, SuperAdmin).
/// </summary>
public sealed record RestoreCurrencyCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;
