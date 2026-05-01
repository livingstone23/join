using JOIN.Application.DTO.Security;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;
using JOIN.Domain.Security;
using Riok.Mapperly.Abstractions;

namespace JOIN.Application.Mappings.Security.RoleSystemOption;

/// <summary>
/// Mapperly implementation for RoleSystemOption entity and DTO projections.
/// </summary>
[Mapper]
public partial class RoleSystemOptionMapper : IRoleSystemOptionMapper
{
    [MapperIgnoreSource(nameof(JOIN.Domain.Security.RoleSystemOption.Role))]
    [MapperIgnoreSource(nameof(JOIN.Domain.Security.RoleSystemOption.SystemOption))]
    [MapperIgnoreSource(nameof(JOIN.Domain.Security.RoleSystemOption.Company))]
    [MapperIgnoreTarget(nameof(RoleSystemOptionDto.RoleName))]
    [MapperIgnoreTarget(nameof(RoleSystemOptionDto.SystemOptionName))]
    [MapperIgnoreTarget(nameof(RoleSystemOptionDto.CompanyName))]
    public partial RoleSystemOptionDto ToDto(JOIN.Domain.Security.RoleSystemOption entity);

    public partial RoleSystemOptionDto ToDto(RoleSystemOptionReadModel model);

    public partial RoleSystemOptionListItemDto ToListItemDto(RoleSystemOptionReadModel model);

    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.Id))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.Created))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.CreatedBy))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.LastModified))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.GcRecord))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.Role))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.SystemOption))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.Company))]
    public partial JOIN.Domain.Security.RoleSystemOption ToEntity(CreateRoleSystemOptionCommand command);

    [MapperIgnoreSource(nameof(UpdateRoleSystemOptionCommand.Id))]
    [MapperIgnoreSource(nameof(UpdateRoleSystemOptionCommand.CompanyId))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.Id))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.CompanyId))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.RoleId))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.SystemOptionId))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.Created))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.CreatedBy))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.LastModified))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.GcRecord))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.Role))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.SystemOption))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.RoleSystemOption.Company))]
    public partial void ApplyUpdate(UpdateRoleSystemOptionCommand command, JOIN.Domain.Security.RoleSystemOption entity);
}
