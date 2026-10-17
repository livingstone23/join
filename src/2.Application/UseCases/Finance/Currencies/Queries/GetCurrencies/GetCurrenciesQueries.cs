using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;
using MediatR;

namespace JOIN.Application.UseCases.Finance.Currencies.Queries;

/// <summary>
/// <c>GET /Currencies</c> (SPEC 56, SuperAdmin): the global ISO catalog, paged.
/// </summary>
public sealed record GetCurrenciesQuery(
    string? Search = null,
    bool? IsActive = null,
    bool? IncludeDeleted = null,
    int? PageNumber = null,
    int? PageSize = null) : IRequest<Response<PagedResult<CurrencyDto>>>;

/// <summary>
/// <c>GET /Currencies/{id}</c>.
/// </summary>
public sealed record GetCurrencyByIdQuery(Guid Id, bool? IncludeDeleted = null) : IRequest<Response<CurrencyDto>>;
