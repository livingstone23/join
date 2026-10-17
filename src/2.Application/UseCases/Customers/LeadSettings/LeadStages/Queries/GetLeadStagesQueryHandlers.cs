using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadStages.Queries;

internal static class LeadStageQuerySql
{
    internal const string Table = "leadstages";
    internal const string Columns = ", t.Probability, t.Kind";
}

/// <summary>Handles <see cref="GetLeadStagesQuery"/>.</summary>
public sealed class GetLeadStagesQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetLeadStagesQuery, Response<IReadOnlyList<LeadStageDto>>>
{
    public Task<Response<IReadOnlyList<LeadStageDto>>> Handle(GetLeadStagesQuery request, CancellationToken cancellationToken) =>
        CommercialCatalogQuery.ListAsync<LeadStageDto>(
            connectionFactory, currentUserService, LeadStageQuerySql.Table, LeadStageQuerySql.Columns,
            request.IncludeInactive, request.IncludeDeleted, request.CompanyId, "Lead stages", cancellationToken);
}

/// <summary>Handles <see cref="GetLeadStageByIdQuery"/>.</summary>
public sealed class GetLeadStageByIdQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetLeadStageByIdQuery, Response<LeadStageDto>>
{
    public Task<Response<LeadStageDto>> Handle(GetLeadStageByIdQuery request, CancellationToken cancellationToken) =>
        CommercialCatalogQuery.GetByIdAsync<LeadStageDto>(
            connectionFactory, currentUserService, LeadStageQuerySql.Table, LeadStageQuerySql.Columns,
            request.Id, request.IncludeDeleted, request.CompanyId, LeadSettingsErrorCodes.LeadStageNotFound, "lead stage", cancellationToken);
}
