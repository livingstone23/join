using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CustomerAddresses.Queries;

/// <summary>
/// Query that retrieves a customer address by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the customer address.</param>
public sealed record GetCustomerAddressByIdQuery(Guid Id) : IRequest<Response<CustomerAddressResponseDto>>;