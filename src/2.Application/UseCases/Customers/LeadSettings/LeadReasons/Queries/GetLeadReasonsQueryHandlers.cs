using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadReasons.Queries;

internal static class LeadReasonQuerySql
{
    internal const string Table = "leadreasons";
    internal const string Columns = ", t.UsedFor";
}

/// <summary>Handles <see cref="GetLeadReasonsQuery"/>.</summary>
public sealed class GetLeadReasonsQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetLeadReasonsQuery, Response<IReadOnlyList<LeadReasonDto>>>
{
    public Task<Response<IReadOnlyList<LeadReasonDto>>> Handle(GetLeadReasonsQuery request, CancellationToken cancellationToken) =>
        CommercialCatalogQuery.ListAsync<LeadReasonDto>(
            connectionFactory, currentUserService, LeadReasonQuerySql.Table, LeadReasonQuerySql.Columns,
            request.IncludeInactive, request.IncludeDeleted, request.CompanyId, "Lead reasons", cancellationToken,
            extraWhere: request.UsedFor is null ? string.Empty : "AND t.UsedFor = @UsedFor",
            parameters: tenantId => new { CompanyId = tenantId, request.UsedFor });
}

/// <summary>Handles <see cref="GetLeadReasonByIdQuery"/>.</summary>
public sealed class GetLeadReasonByIdQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetLeadReasonByIdQuery, Response<LeadReasonDto>>
{
    public Task<Response<LeadReasonDto>> Handle(GetLeadReasonByIdQuery request, CancellationToken cancellationToken) =>
        CommercialCatalogQuery.GetByIdAsync<LeadReasonDto>(
            connectionFactory, currentUserService, LeadReasonQuerySql.Table, LeadReasonQuerySql.Columns,
            request.Id, request.IncludeDeleted, request.CompanyId, LeadSettingsErrorCodes.LeadReasonNotFound, "lead reason", cancellationToken);
}
