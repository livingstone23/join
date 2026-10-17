using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadSources.Queries;

internal static class LeadSourceQuerySql
{
    internal const string Table = "leadsources";
    internal const string Columns = ", t.Description";
}

/// <summary>Handles <see cref="GetLeadSourcesQuery"/>.</summary>
public sealed class GetLeadSourcesQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetLeadSourcesQuery, Response<IReadOnlyList<LeadSourceDto>>>
{
    public Task<Response<IReadOnlyList<LeadSourceDto>>> Handle(GetLeadSourcesQuery request, CancellationToken cancellationToken) =>
        CommercialCatalogQuery.ListAsync<LeadSourceDto>(
            connectionFactory, currentUserService, LeadSourceQuerySql.Table, LeadSourceQuerySql.Columns,
            request.IncludeInactive, request.IncludeDeleted, request.CompanyId, "Lead sources", cancellationToken);
}

/// <summary>Handles <see cref="GetLeadSourceByIdQuery"/>.</summary>
public sealed class GetLeadSourceByIdQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetLeadSourceByIdQuery, Response<LeadSourceDto>>
{
    public Task<Response<LeadSourceDto>> Handle(GetLeadSourceByIdQuery request, CancellationToken cancellationToken) =>
        CommercialCatalogQuery.GetByIdAsync<LeadSourceDto>(
            connectionFactory, currentUserService, LeadSourceQuerySql.Table, LeadSourceQuerySql.Columns,
            request.Id, request.IncludeDeleted, request.CompanyId, LeadSettingsErrorCodes.LeadSourceNotFound, "lead source", cancellationToken);
}
