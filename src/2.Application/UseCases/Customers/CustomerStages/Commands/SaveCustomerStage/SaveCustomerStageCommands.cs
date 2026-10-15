using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;

namespace JOIN.Application.UseCases.Customers.CustomerStages.Commands;

/// <summary>
/// Fields shared by the create and update commands of a customer stage.
/// </summary>
public interface ICustomerStageInput
{
    string Code { get; }
    string Name { get; }
    string? Description { get; }
    int SortOrder { get; }
    bool IsDefault { get; }
    bool IsActive { get; }
}

/// <summary>
/// <c>POST /CustomerStages</c> (SPEC 55): creates a stage of the current company. Marking it as default unmarks the
/// previous default in the same transaction.
/// </summary>
public sealed record CreateCustomerStageCommand : ICustomerStageInput, ITransactionalCommand<Response<CustomerStageDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int SortOrder { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// <c>PUT /CustomerStages/{id}</c> (SPEC 55). The default stage cannot be unmarked without marking another one
/// (<c>CUSTOMER_STAGE_DEFAULT_REQUIRED</c>) nor deactivated (<c>CUSTOMER_STAGE_DEFAULT_INACTIVE</c>).
/// </summary>
public sealed record UpdateCustomerStageCommand : ICustomerStageInput, ITransactionalCommand<Response<CustomerStageDto>>
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int SortOrder { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// <c>DELETE /CustomerStages/{id}</c>: logical delete, blocked while a customer uses it or while it is the default.
/// </summary>
public sealed record DeleteCustomerStageCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary>
/// <c>POST /CustomerStages/{id}/restore</c>: SuperAdmin only (SPEC 41-post). Comes back without <c>IsDefault</c>
/// when the company already has a live default.
/// </summary>
public sealed record RestoreCustomerStageCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
