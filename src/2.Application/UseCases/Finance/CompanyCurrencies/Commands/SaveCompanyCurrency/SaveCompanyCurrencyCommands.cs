using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;

namespace JOIN.Application.UseCases.Finance.CompanyCurrencies.Commands;

/// <summary>
/// <c>POST /CompanyCurrencies</c> (SPEC 56): activates a global currency for the company of the token. With
/// <c>IsBase</c> it becomes the base and the previous base turns parallel (only while the company has no amounts).
/// </summary>
public sealed record CreateCompanyCurrencyCommand : ITransactionalCommand<Response<CompanyCurrencyDto>>
{
    public Guid CurrencyId { get; init; }
    public bool IsBase { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// <c>PUT /CompanyCurrencies/{id}</c> (SPEC 56): changes <c>IsBase</c> and <c>IsActive</c>. The base cannot be
/// unmarked (mark another one instead) nor deactivated (<c>BASE_CURRENCY_REQUIRED</c>).
/// </summary>
public sealed record UpdateCompanyCurrencyCommand : ITransactionalCommand<Response<CompanyCurrencyDto>>
{
    public Guid Id { get; init; }
    public bool IsBase { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// <c>DELETE /CompanyCurrencies/{id}</c>: logical delete; the base cannot be deleted.
/// </summary>
public sealed record DeleteCompanyCurrencyCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary>
/// <c>POST /CompanyCurrencies/{id}/restore</c> (SPEC 41-post, SuperAdmin). Comes back as parallel when the company
/// already has a live base.
/// </summary>
public sealed record RestoreCompanyCurrencyCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
