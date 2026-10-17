using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.RelationshipTypes.Queries;

/// <summary><c>GET /RelationshipTypes</c> (SPEC 59): every row of the company, not paged.</summary>
public sealed record GetRelationshipTypesQuery(
    bool? IncludeInactive = null,
    bool? IncludeDeleted = null,
    Guid? CompanyId = null) : IRequest<Response<IReadOnlyList<RelationshipTypeDto>>>;

/// <summary><c>GET /RelationshipTypes/{id}</c>.</summary>
public sealed record GetRelationshipTypeByIdQuery(Guid Id, bool? IncludeDeleted = null, Guid? CompanyId = null) : IRequest<Response<RelationshipTypeDto>>;
