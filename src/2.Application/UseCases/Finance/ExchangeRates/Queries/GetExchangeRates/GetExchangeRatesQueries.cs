using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;
using MediatR;

namespace JOIN.Application.UseCases.Finance.ExchangeRates.Queries;

/// <summary>
/// <c>GET /ExchangeRates</c> (SPEC 56): the company's rates, newest first, paged.
/// </summary>
public sealed record GetExchangeRatesQuery(
    Guid? CurrencyId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    bool? IncludeDeleted = null,
    int? PageNumber = null,
    int? PageSize = null,
    Guid? CompanyId = null) : IRequest<Response<PagedResult<ExchangeRateDto>>>;

/// <summary>
/// <c>GET /ExchangeRates/{id}</c>.
/// </summary>
public sealed record GetExchangeRateByIdQuery(
    Guid Id,
    bool? IncludeDeleted = null,
    Guid? CompanyId = null) : IRequest<Response<ExchangeRateDto>>;

/// <summary>
/// <c>GET /ExchangeRates/resolve?currencyId=&amp;date=</c> (SPEC 56 rule 4): the rate that applies on the date
/// (today when omitted), so the front can show it before saving an amount.
/// </summary>
public sealed record ResolveExchangeRateQuery(Guid CurrencyId, DateOnly? Date = null) : IRequest<Response<ExchangeRateResolutionDto>>;
