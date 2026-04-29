using JOIN.Domain.Security;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.SystemOptions.Commands;
using Riok.Mapperly.Abstractions;

namespace JOIN.Application.Mappings.Security.SystemOption;

/// <summary>
/// Mapperly implementation for SystemOption entity and DTOs.
/// </summary>
[Mapper]
public partial class SystemOptionMapper : ISystemOptionMapper
{
    [MapperIgnoreSource(nameof(JOIN.Domain.Security.SystemOption.Module))]
    [MapperIgnoreSource(nameof(JOIN.Domain.Security.SystemOption.Parent))]
    [MapperIgnoreSource(nameof(JOIN.Domain.Security.SystemOption.Children))]
    [MapperIgnoreSource(nameof(JOIN.Domain.Security.SystemOption.RoleOptions))]
    public partial SystemOptionDto ToDto(JOIN.Domain.Security.SystemOption entity);

    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.Id))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.Created))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.CreatedBy))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.LastModified))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.GcRecord))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.Module))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.Parent))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.Children))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.RoleOptions))]
    public partial JOIN.Domain.Security.SystemOption ToEntity(CreateSystemOptionCommand command);

    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.Id))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.Created))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.CreatedBy))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.LastModified))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.GcRecord))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.Module))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.Parent))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.Children))]
    [MapperIgnoreTarget(nameof(JOIN.Domain.Security.SystemOption.RoleOptions))]
    [MapperIgnoreSource(nameof(UpdateSystemOptionCommand.Id))]
    public partial void ApplyUpdate(UpdateSystemOptionCommand command, JOIN.Domain.Security.SystemOption entity);
}
