using System.Linq.Expressions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadStages.Commands;

/// <summary>Mapping and messages of the lead stage catalog.</summary>
public sealed class LeadStageRules : CommercialCatalogRules<LeadStage, LeadStageDto>
{
    public static readonly LeadStageRules Instance = new();

    public override string NotFoundCode => LeadSettingsErrorCodes.LeadStageNotFound;
    public override string Label => "lead stage";

    public override LeadStageDto ToDto(LeadStage entity) => new()
    {
        Id = entity.Id,
        GcRecord = entity.GcRecord,
        CompanyId = entity.CompanyId,
        Code = entity.Code,
        Name = entity.Name,
        SortOrder = entity.SortOrder,
        IsActive = entity.IsActive,
        Probability = entity.Probability,
        Kind = (int)entity.Kind,
        CreatedAt = entity.Created
    };

    internal static void ApplyExtra(ILeadStageInput command, LeadStage entity)
    {
        entity.Probability = command.Probability;
        entity.Kind = (LeadStageKind)command.Kind;
    }
}

/// <summary>Handles <see cref="CreateLeadStageCommand"/>.</summary>
public sealed class CreateLeadStageCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService, LeadStageKindCoordinator kinds)
    : CommercialCatalogCreateHandler<CreateLeadStageCommand, LeadStage, LeadStageDto>(unitOfWork, currentUserService, LeadStageRules.Instance)
{
    protected override void ApplyExtra(CreateLeadStageCommand command, LeadStage entity) => LeadStageRules.ApplyExtra(command, entity);

    protected override Response<LeadStageDto>? Validate(CreateLeadStageCommand command, IReadOnlyList<LeadStage> live) =>
        kinds.ValidateCreate<LeadStageDto>((LeadStageKind)command.Kind, live);
}

/// <summary>Handles <see cref="UpdateLeadStageCommand"/>.</summary>
public sealed class UpdateLeadStageCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService, LeadStageKindCoordinator kinds)
    : CommercialCatalogUpdateHandler<UpdateLeadStageCommand, LeadStage, LeadStageDto>(unitOfWork, currentUserService, LeadStageRules.Instance)
{
    protected override void ApplyExtra(UpdateLeadStageCommand command, LeadStage entity) => LeadStageRules.ApplyExtra(command, entity);

    protected override Response<LeadStageDto>? Validate(UpdateLeadStageCommand command, LeadStage existing, IReadOnlyList<LeadStage> live) =>
        kinds.ValidateUpdate<LeadStageDto>(existing, command.Code, (LeadStageKind)command.Kind, command.IsActive, live);
}

/// <summary>Handles <see cref="DeleteLeadStageCommand"/>.</summary>
public sealed class DeleteLeadStageCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService, LeadStageKindCoordinator kinds)
    : CommercialCatalogDeleteHandler<DeleteLeadStageCommand, LeadStage, LeadStageDto>(unitOfWork, currentUserService, LeadStageRules.Instance)
{
    protected override Guid IdOf(DeleteLeadStageCommand command) => command.Id;

    protected override Task<Response<Guid>?> ValidateAsync(LeadStage entity, CancellationToken cancellationToken) =>
        Task.FromResult(kinds.ValidateDelete<Guid>(entity));
}

/// <summary>
/// Handles <see cref="RestoreLeadStageCommand"/>: a deleted Open stage comes back unless its code or name is taken.
/// </summary>
public sealed class RestoreLeadStageCommandHandler(ICurrentUserService currentUserService, SoftDeleteRestorer restorer)
    : CommercialCatalogRestoreHandler<RestoreLeadStageCommand, LeadStage>(currentUserService, restorer)
{
    protected override Guid IdOf(RestoreLeadStageCommand command) => command.Id;
    protected override Guid? CompanyOf(RestoreLeadStageCommand command) => command.CompanyId;

    protected override Expression<Func<LeadStage, bool>> DuplicateOf(LeadStage entity)
    {
        var kind = entity.Kind;
        var uniqueKind = kind != LeadStageKind.Open;
        return x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId
            && (x.Code == entity.Code || x.Name == entity.Name || (uniqueKind && x.Kind == kind));
    }
}
