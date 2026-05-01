using JOIN.Application.DTO.Security;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

namespace JOIN.Application.Mappings.Security.RoleSystemOption;

/// <summary>
/// Mapper contract for RoleSystemOption conversions between entity, commands, and DTOs.
/// </summary>
public interface IRoleSystemOptionMapper
{
    RoleSystemOptionDto ToDto(JOIN.Domain.Security.RoleSystemOption entity);
    RoleSystemOptionDto ToDto(RoleSystemOptionReadModel model);
    RoleSystemOptionListItemDto ToListItemDto(RoleSystemOptionReadModel model);
    JOIN.Domain.Security.RoleSystemOption ToEntity(CreateRoleSystemOptionCommand command);
    void ApplyUpdate(UpdateRoleSystemOptionCommand command, JOIN.Domain.Security.RoleSystemOption entity);
}
