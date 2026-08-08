using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.Roles.Commands.CreateRole;
using JOIN.Application.UseCases.Security.Roles.Commands.UpdateRole;
using Riok.Mapperly.Abstractions;

namespace JOIN.Application.Mappings.Security;

/// <summary>
/// Mapperly contract for ApplicationRole <-> RoleDto and command mapping.
/// </summary>
public interface IRoleMapper
{
    RoleDto FromEntity(Domain.Security.ApplicationRole role);

    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.Id))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.NormalizedName))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.Created))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.CreatedBy))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.LastModified))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.GcRecord))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.UserRoleCompanies))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.RoleSystemOptions))]
    Domain.Security.ApplicationRole ToEntity(CreateRoleCommand command);

    /// <summary>
    /// Applies user-editable fields from the command onto a tracked entity. Name is mapped to Name;
    /// the handler is responsible for normalizing and stamping LastModified/LastModifiedBy.
    /// </summary>
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.Id))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.NormalizedName))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.Created))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.CreatedBy))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.LastModified))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.GcRecord))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.UserRoleCompanies))]
    [MapperIgnoreTarget(nameof(Domain.Security.ApplicationRole.RoleSystemOptions))]
    [MapperIgnoreSource(nameof(UpdateRoleCommand.Id))]
    void ApplyUpdate(UpdateRoleCommand command, Domain.Security.ApplicationRole role);
}

/// <summary>
/// Mapperly source-generated implementation for ApplicationRole mapping.
/// </summary>
[Mapper]
public partial class RoleMapper : IRoleMapper
{
    public partial RoleDto FromEntity(Domain.Security.ApplicationRole role);

    public partial Domain.Security.ApplicationRole ToEntity(CreateRoleCommand command);

    public partial void ApplyUpdate(UpdateRoleCommand command, Domain.Security.ApplicationRole role);
}
