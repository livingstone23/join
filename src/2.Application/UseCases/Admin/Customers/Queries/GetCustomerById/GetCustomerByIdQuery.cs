using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Customers.Queries;

/// <summary>
/// Query to retrieve a single customer by identifier.
/// </summary>
public sealed record GetCustomerByIdQuery(Guid Id) : IRequest<Response<CustomerResponseDto>>;
