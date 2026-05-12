using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CustomerContacts.Queries;

/// <summary>
/// Query that retrieves all active contacts for a given customer in the current tenant.
/// </summary>
/// <param name="CustomerId">The unique identifier of the customer.</param>
public sealed record GetCustomerContactsByCustomerIdQuery(Guid CustomerId) : IRequest<Response<List<CustomerContactResponseDto>>>;
