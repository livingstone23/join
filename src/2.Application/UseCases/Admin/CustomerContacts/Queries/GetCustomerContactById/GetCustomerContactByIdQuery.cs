using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CustomerContacts.Queries;

/// <summary>
/// Query that retrieves a single customer contact by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the customer contact.</param>
public sealed record GetCustomerContactByIdQuery(Guid Id) : IRequest<Response<CustomerContactResponseDto>>;
