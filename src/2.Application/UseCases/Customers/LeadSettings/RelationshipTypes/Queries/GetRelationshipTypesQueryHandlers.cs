using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.RelationshipTypes.Queries;

internal static class RelationshipTypeQuerySql
{
    internal const string Table = "relationshiptypes";
    internal const string Columns = ", t.OnWin";
}

/// <summary>Handles <see cref="GetRelationshipTypesQuery"/>.</summary>
public sealed class GetRelationshipTypesQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetRelationshipTypesQuery, Response<IReadOnlyList<RelationshipTypeDto>>>
{
    public Task<Response<IReadOnlyList<RelationshipTypeDto>>> Handle(GetRelationshipTypesQuery request, CancellationToken cancellationToken) =>
        CommercialCatalogQuery.ListAsync<RelationshipTypeDto>(
            connectionFactory, currentUserService, RelationshipTypeQuerySql.Table, RelationshipTypeQuerySql.Columns,
            request.IncludeInactive, request.IncludeDeleted, request.CompanyId, "Relationship types", cancellationToken);
}

/// <summary>Handles <see cref="GetRelationshipTypeByIdQuery"/>.</summary>
public sealed class GetRelationshipTypeByIdQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetRelationshipTypeByIdQuery, Response<RelationshipTypeDto>>
{
    public Task<Response<RelationshipTypeDto>> Handle(GetRelationshipTypeByIdQuery request, CancellationToken cancellationToken) =>
        CommercialCatalogQuery.GetByIdAsync<RelationshipTypeDto>(
            connectionFactory, currentUserService, RelationshipTypeQuerySql.Table, RelationshipTypeQuerySql.Columns,
            request.Id, request.IncludeDeleted, request.CompanyId, LeadSettingsErrorCodes.RelationshipTypeNotFound, "relationship type", cancellationToken);
}
