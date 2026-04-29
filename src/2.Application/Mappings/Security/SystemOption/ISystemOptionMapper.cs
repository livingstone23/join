using JOIN.Domain.Security;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.SystemOptions.Commands;

namespace JOIN.Application.Mappings.Security.SystemOption;

/// <summary>
/// Mapper interface for SystemOption entity and DTOs.
/// </summary>
public interface ISystemOptionMapper
{
    SystemOptionDto ToDto(JOIN.Domain.Security.SystemOption entity);
    JOIN.Domain.Security.SystemOption ToEntity(CreateSystemOptionCommand command);
    void ApplyUpdate(UpdateSystemOptionCommand command, JOIN.Domain.Security.SystemOption entity);
}
