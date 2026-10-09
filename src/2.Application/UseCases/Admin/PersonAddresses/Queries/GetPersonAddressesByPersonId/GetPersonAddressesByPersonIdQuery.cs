using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonAddresses.Queries;

/// <summary>
/// Query that retrieves all addresses for a customer constrained to the current tenant.
/// </summary>
/// <param name="PersonId">The unique identifier of the customer owner.</param>
public sealed record GetPersonAddressesByPersonIdQuery(Guid PersonId, bool? IncludeDeleted = null, Guid? CompanyId = null)
    : IRequest<Response<List<PersonAddressResponseDto>>>;
