using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadReasons.Queries;

/// <summary><c>GET /LeadReasons</c> (SPEC 59): every row of the company, not paged.</summary>
public sealed record GetLeadReasonsQuery(
    bool? IncludeInactive = null,
    bool? IncludeDeleted = null,
    Guid? CompanyId = null, int? UsedFor = null) : IRequest<Response<IReadOnlyList<LeadReasonDto>>>;

/// <summary><c>GET /LeadReasons/{id}</c>.</summary>
public sealed record GetLeadReasonByIdQuery(Guid Id, bool? IncludeDeleted = null, Guid? CompanyId = null) : IRequest<Response<LeadReasonDto>>;
