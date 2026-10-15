using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;
using MediatR;

namespace JOIN.Application.UseCases.Customers.Customers.Queries;

/// <summary>
/// Paginated customer list query for the current tenant.
/// </summary>
public sealed record GetCustomersPagedQuery(
    int PageNumber = 1,
    int PageSize = 10,
    string? CustomerCode = null,
    Guid? CustomerStageId = null,
    bool? IsActive = null,
    string? PersonName = null,
    string? UserEmail = null,
    bool? IncludeDeleted = null,
    Guid? CompanyId = null) : IRequest<Response<PagedResult<CustomerResponseDto>>>;
