using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;
using MediatR;

namespace JOIN.Application.UseCases.Customers.Customers.Queries;

/// <summary>
/// Query to retrieve a single customer by identifier.
/// </summary>
public sealed record GetCustomerByIdQuery(Guid Id, bool? IncludeDeleted = null, Guid? CompanyId = null) : IRequest<Response<CustomerResponseDto>>;
