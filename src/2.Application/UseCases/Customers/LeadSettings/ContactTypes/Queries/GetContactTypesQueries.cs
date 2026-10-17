using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.ContactTypes.Queries;

/// <summary><c>GET /ContactTypes</c> (SPEC 59, SuperAdmin): the global master, not paged.</summary>
public sealed record GetContactTypesQuery(bool? IncludeInactive = null, bool? IncludeDeleted = null) : IRequest<Response<IReadOnlyList<ContactTypeDto>>>;

/// <summary><c>GET /ContactTypes/{id}</c>.</summary>
public sealed record GetContactTypeByIdQuery(Guid Id, bool? IncludeDeleted = null) : IRequest<Response<ContactTypeDto>>;
