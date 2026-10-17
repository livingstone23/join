using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadSources.Commands;

/// <summary><c>POST /LeadSources</c> (SPEC 59).</summary>
public sealed record CreateLeadSourceCommand : ICommercialCatalogInput, ITransactionalCommand<Response<LeadSourceDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public string? Description { get; init; }
}

/// <summary><c>PUT /LeadSources/{id}</c> (SPEC 59).</summary>
public sealed record UpdateLeadSourceCommand : ICommercialCatalogUpdate, ITransactionalCommand<Response<LeadSourceDto>>
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public string? Description { get; init; }
}

/// <summary><c>DELETE /LeadSources/{id}</c>: logical delete. SPEC 61 adds the in-use block.</summary>
public sealed record DeleteLeadSourceCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary><c>POST /LeadSources/{id}/restore</c> (SPEC 41-post, SuperAdmin).</summary>
public sealed record RestoreLeadSourceCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
