using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;
using MediatR;

namespace JOIN.Application.UseCases.Customers.CustomerStages.Queries;

/// <summary>
/// <c>GET /CustomerStages</c> (SPEC 55): the company's stages ordered by <c>SortOrder</c>, <c>Name</c>, not paginated.
/// </summary>
public sealed record GetCustomerStagesQuery(
    bool? IncludeInactive = null,
    bool? IncludeDeleted = null,
    Guid? CompanyId = null) : IRequest<Response<IReadOnlyList<CustomerStageDto>>>;

/// <summary>
/// <c>GET /CustomerStages/{id}</c>.
/// </summary>
public sealed record GetCustomerStageByIdQuery(
    Guid Id,
    bool? IncludeDeleted = null,
    Guid? CompanyId = null) : IRequest<Response<CustomerStageDto>>;
