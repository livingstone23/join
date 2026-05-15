using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Queries;

public sealed record GetTaxRegimesQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? Code = null,
    string? Name = null,
    bool? IsActive = null)
    : IRequest<Response<PagedResult<TaxRegimeDto>>>;
