using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;

namespace JOIN.Application.UseCases.Customers.LeadSettings.RelationshipTypes.Commands;

/// <summary><c>POST /RelationshipTypes</c> (SPEC 59).</summary>
public sealed record CreateRelationshipTypeCommand : ICommercialCatalogInput, ITransactionalCommand<Response<RelationshipTypeDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    /// <summary><c>RelationshipOnWin</c>: 0 None, 1 Customer.</summary>
    public int OnWin { get; init; }
}

/// <summary><c>PUT /RelationshipTypes/{id}</c> (SPEC 59).</summary>
public sealed record UpdateRelationshipTypeCommand : ICommercialCatalogUpdate, ITransactionalCommand<Response<RelationshipTypeDto>>
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    /// <summary><c>RelationshipOnWin</c>: 0 None, 1 Customer.</summary>
    public int OnWin { get; init; }
}

/// <summary><c>DELETE /RelationshipTypes/{id}</c>: logical delete. SPEC 61 adds the in-use block.</summary>
public sealed record DeleteRelationshipTypeCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary><c>POST /RelationshipTypes/{id}/restore</c> (SPEC 41-post, SuperAdmin).</summary>
public sealed record RestoreRelationshipTypeCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
