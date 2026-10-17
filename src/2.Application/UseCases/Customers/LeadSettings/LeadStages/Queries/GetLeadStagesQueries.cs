using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadStages.Queries;

/// <summary><c>GET /LeadStages</c> (SPEC 59): every row of the company, not paged.</summary>
public sealed record GetLeadStagesQuery(
    bool? IncludeInactive = null,
    bool? IncludeDeleted = null,
    Guid? CompanyId = null) : IRequest<Response<IReadOnlyList<LeadStageDto>>>;

/// <summary><c>GET /LeadStages/{id}</c>.</summary>
public sealed record GetLeadStageByIdQuery(Guid Id, bool? IncludeDeleted = null, Guid? CompanyId = null) : IRequest<Response<LeadStageDto>>;
