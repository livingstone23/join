using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadReasons.Commands;

/// <summary>Mapping and messages of the lead reason catalog.</summary>
public sealed class LeadReasonRules : CommercialCatalogRules<LeadReason, LeadReasonDto>
{
    public static readonly LeadReasonRules Instance = new();

    public override string NotFoundCode => LeadSettingsErrorCodes.LeadReasonNotFound;
    public override string Label => "lead reason";

    public override LeadReasonDto ToDto(LeadReason entity) => new()
    {
        Id = entity.Id,
        GcRecord = entity.GcRecord,
        CompanyId = entity.CompanyId,
        Code = entity.Code,
        Name = entity.Name,
        SortOrder = entity.SortOrder,
        IsActive = entity.IsActive,
        UsedFor = (int)entity.UsedFor,
        CreatedAt = entity.Created
    };
}

/// <summary>Handles <see cref="CreateLeadReasonCommand"/>.</summary>
public sealed class CreateLeadReasonCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : CommercialCatalogCreateHandler<CreateLeadReasonCommand, LeadReason, LeadReasonDto>(unitOfWork, currentUserService, LeadReasonRules.Instance)
{
    protected override void ApplyExtra(CreateLeadReasonCommand command, LeadReason entity) => entity.UsedFor = (LeadReasonUsage)command.UsedFor;
}

/// <summary>Handles <see cref="UpdateLeadReasonCommand"/>.</summary>
public sealed class UpdateLeadReasonCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : CommercialCatalogUpdateHandler<UpdateLeadReasonCommand, LeadReason, LeadReasonDto>(unitOfWork, currentUserService, LeadReasonRules.Instance)
{
    protected override void ApplyExtra(UpdateLeadReasonCommand command, LeadReason entity) => entity.UsedFor = (LeadReasonUsage)command.UsedFor;
}

/// <summary>Handles <see cref="DeleteLeadReasonCommand"/>.</summary>
public sealed class DeleteLeadReasonCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : CommercialCatalogDeleteHandler<DeleteLeadReasonCommand, LeadReason, LeadReasonDto>(unitOfWork, currentUserService, LeadReasonRules.Instance)
{
    protected override Guid IdOf(DeleteLeadReasonCommand command) => command.Id;
}

/// <summary>Handles <see cref="RestoreLeadReasonCommand"/> with <see cref="SoftDeleteRestorer"/> (SPEC 41-post).</summary>
public sealed class RestoreLeadReasonCommandHandler(ICurrentUserService currentUserService, SoftDeleteRestorer restorer)
    : CommercialCatalogRestoreHandler<RestoreLeadReasonCommand, LeadReason>(currentUserService, restorer)
{
    protected override Guid IdOf(RestoreLeadReasonCommand command) => command.Id;
    protected override Guid? CompanyOf(RestoreLeadReasonCommand command) => command.CompanyId;
}
