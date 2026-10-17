using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadReasons.Commands;

/// <summary><c>POST /LeadReasons</c> (SPEC 59).</summary>
public sealed record CreateLeadReasonCommand : ICommercialCatalogInput, ITransactionalCommand<Response<LeadReasonDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    /// <summary><c>LeadReasonUsage</c>: 1 Won, 2 Lost, 3 Inactive.</summary>
    public int UsedFor { get; init; } = 2;
}

/// <summary><c>PUT /LeadReasons/{id}</c> (SPEC 59).</summary>
public sealed record UpdateLeadReasonCommand : ICommercialCatalogUpdate, ITransactionalCommand<Response<LeadReasonDto>>
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    /// <summary><c>LeadReasonUsage</c>: 1 Won, 2 Lost, 3 Inactive.</summary>
    public int UsedFor { get; init; } = 2;
}

/// <summary><c>DELETE /LeadReasons/{id}</c>: logical delete. SPEC 61 adds the in-use block.</summary>
public sealed record DeleteLeadReasonCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary><c>POST /LeadReasons/{id}/restore</c> (SPEC 41-post, SuperAdmin).</summary>
public sealed record RestoreLeadReasonCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
