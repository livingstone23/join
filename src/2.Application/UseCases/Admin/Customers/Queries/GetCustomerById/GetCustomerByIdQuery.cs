


using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Customers.Queries;



/// <summary>
/// Query to retrieve a specific customer by their unique identifier.
/// Implements IRequest to be routed by MediatR.
/// </summary>
/// <param name="CustomerId">The unique identifier of the customer.</param>
public record GetCustomerByIdQuery(Guid CustomerId) : IRequest<Response<CustomerDto>>;


