


using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Customers.Commands;



/// <summary>
/// Command to register a new Customer in the system.
/// Carries the DTO containing the required information.
/// </summary>
/// <param name="CustomerDto">The customer data transfer object.</param>
public record CreateCustomerCommand(CreateCustomerDto CustomerDto) : IRequest<Response<Guid>>;



