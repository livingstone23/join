using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Customers.Queries;

/// <summary>
/// Paginated customer list query for the current tenant.
/// </summary>
public sealed record GetCustomersPagedQuery(
    int PageNumber = 1,
    int PageSize = 10,
    string? CustomerCode = null,
    PersonLifecycleStage? PersonLifecycleStage = null,
    bool? IsActive = null,
    string? PersonName = null,
    string? UserEmail = null) : IRequest<Response<PagedResult<CustomerResponseDto>>>;
