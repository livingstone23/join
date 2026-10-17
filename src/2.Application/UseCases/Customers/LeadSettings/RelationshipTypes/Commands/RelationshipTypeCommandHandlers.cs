using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.LeadSettings.RelationshipTypes.Commands;

/// <summary>Mapping and messages of the relationship type catalog.</summary>
public sealed class RelationshipTypeRules : CommercialCatalogRules<RelationshipType, RelationshipTypeDto>
{
    public static readonly RelationshipTypeRules Instance = new();

    public override string NotFoundCode => LeadSettingsErrorCodes.RelationshipTypeNotFound;
    public override string Label => "relationship type";

    public override RelationshipTypeDto ToDto(RelationshipType entity) => new()
    {
        Id = entity.Id,
        GcRecord = entity.GcRecord,
        CompanyId = entity.CompanyId,
        Code = entity.Code,
        Name = entity.Name,
        SortOrder = entity.SortOrder,
        IsActive = entity.IsActive,
        OnWin = (int)entity.OnWin,
        CreatedAt = entity.Created
    };
}

/// <summary>Handles <see cref="CreateRelationshipTypeCommand"/>.</summary>
public sealed class CreateRelationshipTypeCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : CommercialCatalogCreateHandler<CreateRelationshipTypeCommand, RelationshipType, RelationshipTypeDto>(unitOfWork, currentUserService, RelationshipTypeRules.Instance)
{
    protected override void ApplyExtra(CreateRelationshipTypeCommand command, RelationshipType entity) => entity.OnWin = (RelationshipOnWin)command.OnWin;
}

/// <summary>Handles <see cref="UpdateRelationshipTypeCommand"/>.</summary>
public sealed class UpdateRelationshipTypeCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : CommercialCatalogUpdateHandler<UpdateRelationshipTypeCommand, RelationshipType, RelationshipTypeDto>(unitOfWork, currentUserService, RelationshipTypeRules.Instance)
{
    protected override void ApplyExtra(UpdateRelationshipTypeCommand command, RelationshipType entity) => entity.OnWin = (RelationshipOnWin)command.OnWin;
}

/// <summary>Handles <see cref="DeleteRelationshipTypeCommand"/>: not while a live lead uses it (SPEC 61 B.14).</summary>
public sealed class DeleteRelationshipTypeCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : CommercialCatalogDeleteHandler<DeleteRelationshipTypeCommand, RelationshipType, RelationshipTypeDto>(unitOfWork, currentUserService, RelationshipTypeRules.Instance)
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    protected override Guid IdOf(DeleteRelationshipTypeCommand command) => command.Id;

    protected override async Task<Response<Guid>?> ValidateAsync(RelationshipType entity, CancellationToken cancellationToken)
    {
        var check = await new ActiveDependentsCheck(_unitOfWork).CountAsync<Lead>(l => l.GcRecord == 0 && l.RelationshipTypeId == entity.Id, "Leads");
        return check.HasDependents ? Response<Guid>.Error(LeadErrorCodes.RelationshipTypeInUse, [.. check.Details]) : null;
    }
}

/// <summary>Handles <see cref="RestoreRelationshipTypeCommand"/> with <see cref="SoftDeleteRestorer"/> (SPEC 41-post).</summary>
public sealed class RestoreRelationshipTypeCommandHandler(ICurrentUserService currentUserService, SoftDeleteRestorer restorer)
    : CommercialCatalogRestoreHandler<RestoreRelationshipTypeCommand, RelationshipType>(currentUserService, restorer)
{
    protected override Guid IdOf(RestoreRelationshipTypeCommand command) => command.Id;
    protected override Guid? CompanyOf(RestoreRelationshipTypeCommand command) => command.CompanyId;
}
