using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;
using MediatR;

namespace JOIN.Application.UseCases.Finance.CompanyCurrencies.Queries;

/// <summary>
/// <c>GET /CompanyCurrencies</c> (SPEC 56): the currencies of the company, base first, not paginated, with the latest
/// rate on or before today.
/// </summary>
public sealed record GetCompanyCurrenciesQuery(
    bool? IncludeInactive = null,
    bool? IncludeDeleted = null,
    Guid? CompanyId = null) : IRequest<Response<IReadOnlyList<CompanyCurrencyDto>>>;

/// <summary>
/// <c>GET /CompanyCurrencies/{id}</c>.
/// </summary>
public sealed record GetCompanyCurrencyByIdQuery(
    Guid Id,
    bool? IncludeDeleted = null,
    Guid? CompanyId = null) : IRequest<Response<CompanyCurrencyDto>>;
