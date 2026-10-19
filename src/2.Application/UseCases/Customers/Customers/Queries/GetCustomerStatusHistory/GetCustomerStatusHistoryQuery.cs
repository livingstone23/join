using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;
using MediatR;

namespace JOIN.Application.UseCases.Customers.Customers.Queries;

/// <summary>
/// <c>GET /Customers/{id}/status-history</c> (SPEC 55): the customer's status events and stage changes (SPEC 66), newest first.
/// </summary>
public sealed record GetCustomerStatusHistoryQuery(
    Guid CustomerId,
    int? PageNumber = null,
    int? PageSize = null) : IRequest<Response<PagedResult<CustomerStatusHistoryDto>>>;
