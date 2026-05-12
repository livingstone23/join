using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CustomerAddresses.Queries;

/// <summary>
/// Query that retrieves all addresses for a customer constrained to the current tenant.
/// </summary>
/// <param name="CustomerId">The unique identifier of the customer owner.</param>
public sealed record GetCustomerAddressesByCustomerIdQuery(Guid CustomerId)
    : IRequest<Response<List<CustomerAddressResponseDto>>>;
