using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadSources.Commands;

/// <summary>Mapping and messages of the lead source catalog.</summary>
public sealed class LeadSourceRules : CommercialCatalogRules<LeadSource, LeadSourceDto>
{
    public static readonly LeadSourceRules Instance = new();

    public override string NotFoundCode => LeadSettingsErrorCodes.LeadSourceNotFound;
    public override string Label => "lead source";

    public override LeadSourceDto ToDto(LeadSource entity) => new()
    {
        Id = entity.Id,
        GcRecord = entity.GcRecord,
        CompanyId = entity.CompanyId,
        Code = entity.Code,
        Name = entity.Name,
        SortOrder = entity.SortOrder,
        IsActive = entity.IsActive,
        Description = entity.Description,
        CreatedAt = entity.Created
    };
}

/// <summary>Handles <see cref="CreateLeadSourceCommand"/>.</summary>
public sealed class CreateLeadSourceCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : CommercialCatalogCreateHandler<CreateLeadSourceCommand, LeadSource, LeadSourceDto>(unitOfWork, currentUserService, LeadSourceRules.Instance)
{
    protected override void ApplyExtra(CreateLeadSourceCommand command, LeadSource entity) => entity.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
}

/// <summary>Handles <see cref="UpdateLeadSourceCommand"/>.</summary>
public sealed class UpdateLeadSourceCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : CommercialCatalogUpdateHandler<UpdateLeadSourceCommand, LeadSource, LeadSourceDto>(unitOfWork, currentUserService, LeadSourceRules.Instance)
{
    protected override void ApplyExtra(UpdateLeadSourceCommand command, LeadSource entity) => entity.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
}

/// <summary>Handles <see cref="DeleteLeadSourceCommand"/>: not while a live lead uses it (SPEC 61 B.14).</summary>
public sealed class DeleteLeadSourceCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : CommercialCatalogDeleteHandler<DeleteLeadSourceCommand, LeadSource, LeadSourceDto>(unitOfWork, currentUserService, LeadSourceRules.Instance)
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    protected override Guid IdOf(DeleteLeadSourceCommand command) => command.Id;

    protected override async Task<Response<Guid>?> ValidateAsync(LeadSource entity, CancellationToken cancellationToken)
    {
        var check = await new ActiveDependentsCheck(_unitOfWork).CountAsync<Lead>(l => l.GcRecord == 0 && l.LeadSourceId == entity.Id, "Leads");
        return check.HasDependents ? Response<Guid>.Error(LeadErrorCodes.LeadSourceInUse, [.. check.Details]) : null;
    }
}

/// <summary>Handles <see cref="RestoreLeadSourceCommand"/> with <see cref="SoftDeleteRestorer"/> (SPEC 41-post).</summary>
public sealed class RestoreLeadSourceCommandHandler(ICurrentUserService currentUserService, SoftDeleteRestorer restorer)
    : CommercialCatalogRestoreHandler<RestoreLeadSourceCommand, LeadSource>(currentUserService, restorer)
{
    protected override Guid IdOf(RestoreLeadSourceCommand command) => command.Id;
    protected override Guid? CompanyOf(RestoreLeadSourceCommand command) => command.CompanyId;
}
