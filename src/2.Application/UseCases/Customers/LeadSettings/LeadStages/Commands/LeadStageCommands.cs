using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadStages.Commands;

/// <summary>Fields of a lead stage beyond the shared catalog ones.</summary>
public interface ILeadStageInput : ICommercialCatalogInput
{
    int Probability { get; }
    /// <summary><c>LeadStageKind</c>: 1 Initial, 2 Open, 3 Won, 4 Lost.</summary>
    int Kind { get; }
}

/// <summary><c>POST /LeadStages</c> (SPEC 59).</summary>
public sealed record CreateLeadStageCommand : ILeadStageInput, ITransactionalCommand<Response<LeadStageDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public int Probability { get; init; }
    public int Kind { get; init; } = 2;
}

/// <summary><c>PUT /LeadStages/{id}</c> (SPEC 59).</summary>
public sealed record UpdateLeadStageCommand : ILeadStageInput, ICommercialCatalogUpdate, ITransactionalCommand<Response<LeadStageDto>>
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public int Probability { get; init; }
    public int Kind { get; init; } = 2;
}

/// <summary><c>DELETE /LeadStages/{id}</c>: logical delete. SPEC 61 adds the in-use block.</summary>
public sealed record DeleteLeadStageCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary><c>POST /LeadStages/{id}/restore</c> (SPEC 41-post, SuperAdmin).</summary>
public sealed record RestoreLeadStageCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
