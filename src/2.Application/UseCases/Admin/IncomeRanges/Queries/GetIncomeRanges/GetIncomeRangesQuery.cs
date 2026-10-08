using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IncomeRanges.Queries;

public sealed record GetIncomeRangesQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? DisplayName = null,
    string? CurrencyCode = null,
    bool? IsActive = null,
    bool? IncludeDeleted = null,
    Guid? CompanyId = null)
    : IRequest<Response<PagedResult<IncomeRangeDto>>>;
