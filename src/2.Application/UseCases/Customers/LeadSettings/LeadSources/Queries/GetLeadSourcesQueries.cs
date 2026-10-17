using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadSources.Queries;

/// <summary><c>GET /LeadSources</c> (SPEC 59): every row of the company, not paged.</summary>
public sealed record GetLeadSourcesQuery(
    bool? IncludeInactive = null,
    bool? IncludeDeleted = null,
    Guid? CompanyId = null) : IRequest<Response<IReadOnlyList<LeadSourceDto>>>;

/// <summary><c>GET /LeadSources/{id}</c>.</summary>
public sealed record GetLeadSourceByIdQuery(Guid Id, bool? IncludeDeleted = null, Guid? CompanyId = null) : IRequest<Response<LeadSourceDto>>;
