using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.SalesTeams;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using JOIN.Domain.Customers.Teams;

namespace JOIN.Application.UseCases.Customers.SalesTeams.TeamRoles.Commands;

/// <summary>Mapping and messages of the management role catalog.</summary>
public sealed class TeamRoleRules : CommercialCatalogRules<TeamRole, TeamRoleDto>
{
    public static readonly TeamRoleRules Instance = new();

    public override string NotFoundCode => SalesTeamErrorCodes.TeamRoleNotFound;
    public override string Label => "team role";

    public override TeamRoleDto ToDto(TeamRole entity) => new()
    {
        Id = entity.Id,
        GcRecord = entity.GcRecord,
        CompanyId = entity.CompanyId,
        Code = entity.Code,
        Name = entity.Name,
        SortOrder = entity.SortOrder,
        IsActive = entity.IsActive,
        LeadScope = (int)entity.LeadScope,
        IsLeader = entity.IsLeader,
        CanReassign = entity.CanReassign,
        CreatedAt = entity.Created
    };

    internal static void ApplyExtra(ITeamRoleInput command, TeamRole entity)
    {
        entity.LeadScope = (LeadScope)command.LeadScope;
        entity.IsLeader = command.IsLeader;
        entity.CanReassign = command.CanReassign;
    }
}

/// <summary>Handles <see cref="CreateTeamRoleCommand"/>.</summary>
public sealed class CreateTeamRoleCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : CommercialCatalogCreateHandler<CreateTeamRoleCommand, TeamRole, TeamRoleDto>(unitOfWork, currentUserService, TeamRoleRules.Instance)
{
    protected override void ApplyExtra(CreateTeamRoleCommand command, TeamRole entity) => TeamRoleRules.ApplyExtra(command, entity);
}

/// <summary>Handles <see cref="UpdateTeamRoleCommand"/>.</summary>
public sealed class UpdateTeamRoleCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : CommercialCatalogUpdateHandler<UpdateTeamRoleCommand, TeamRole, TeamRoleDto>(unitOfWork, currentUserService, TeamRoleRules.Instance)
{
    protected override void ApplyExtra(UpdateTeamRoleCommand command, TeamRole entity) => TeamRoleRules.ApplyExtra(command, entity);
}

/// <summary>Handles <see cref="DeleteTeamRoleCommand"/>: a role held by a current membership stays (SPEC 60 B.8).</summary>
public sealed class DeleteTeamRoleCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : CommercialCatalogDeleteHandler<DeleteTeamRoleCommand, TeamRole, TeamRoleDto>(unitOfWork, currentUserService, TeamRoleRules.Instance)
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    protected override Guid IdOf(DeleteTeamRoleCommand command) => command.Id;

    protected override async Task<Response<Guid>?> ValidateAsync(TeamRole entity, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var holders = (await _unitOfWork.GetRepository<SalesTeamMember>().GetAllIncludingDeletedAsync(m =>
                m.TeamRoleId == entity.Id && m.GcRecord == 0 && (m.ValidTo == null || m.ValidTo >= today)))
            .Count();

        return holders > 0
            ? Response<Guid>.Error(SalesTeamErrorCodes.TeamRoleInUse, [$"Active SalesTeamMembers: {holders}"])
            : null;
    }
}

/// <summary>Handles <see cref="RestoreTeamRoleCommand"/> with <see cref="SoftDeleteRestorer"/> (SPEC 41-post).</summary>
public sealed class RestoreTeamRoleCommandHandler(ICurrentUserService currentUserService, SoftDeleteRestorer restorer)
    : CommercialCatalogRestoreHandler<RestoreTeamRoleCommand, TeamRole>(currentUserService, restorer)
{
    protected override Guid IdOf(RestoreTeamRoleCommand command) => command.Id;
    protected override Guid? CompanyOf(RestoreTeamRoleCommand command) => command.CompanyId;
}
