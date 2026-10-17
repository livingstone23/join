using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.Leads;
using MediatR;

namespace JOIN.Application.UseCases.Customers.Leads.Queries;

/// <summary>
/// <c>GET /Leads</c> (SPEC 61 C.16): the leads the user can see, paged, newest contact first. <c>Status</c> defaults to
/// 1 (Open); 0 lists every status.
/// </summary>
public sealed record GetLeadsQuery(
    int? Status = null,
    Guid? LeadStageId = null,
    Guid? OwnerUserId = null,
    Guid? SalesTeamId = null,
    Guid? LeadSourceId = null,
    Guid? RelationshipTypeId = null,
    string? Search = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int? PageNumber = null,
    int? PageSize = null) : IRequest<Response<PagedResult<LeadListItemDto>>>;

/// <summary><c>GET /Leads/board</c> (SPEC 61 C.17): per open stage, count, base value and the first 20 cards.</summary>
public sealed record GetLeadBoardQuery(Guid? SalesTeamId = null, Guid? OwnerUserId = null) : IRequest<Response<IReadOnlyList<LeadBoardColumnDto>>>;

/// <summary><c>GET /Leads/{id}</c> (SPEC 61 C.18). Out of scope → <c>LEAD_NOT_FOUND</c>.</summary>
public sealed record GetLeadByIdQuery(Guid Id) : IRequest<Response<LeadDetailDto>>;

/// <summary><c>GET /Leads/{id}/timeline</c> (SPEC 61 C.19): contacts, comments, documents (SPEC 62, <c>RefId</c> = contact), stage, owner and status changes, newest first.</summary>
public sealed record GetLeadTimelineQuery(Guid Id, int? Limit = null) : IRequest<Response<IReadOnlyList<LeadTimelineItemDto>>>;

/// <summary><c>GET /Leads/{id}/interactions</c>: the contacts of a lead, newest first.</summary>
public sealed record GetLeadInteractionsQuery(Guid Id) : IRequest<Response<IReadOnlyList<LeadInteractionDto>>>;

/// <summary><c>GET /Leads/duplicates?personId=</c> (SPEC 61 C.20): open leads of the person (warning before creating).</summary>
public sealed record GetLeadDuplicatesQuery(Guid PersonId) : IRequest<Response<IReadOnlyList<LeadDuplicateDto>>>;

/// <summary><c>GET /Leads/catalogs</c> (SPEC 61 C.21): active catalogs and currencies of the company.</summary>
public sealed record GetLeadCatalogsQuery : IRequest<Response<LeadCatalogsDto>>;
