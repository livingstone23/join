# Blueprint: Riok.Mapperly Mapper (High-Performance Mapping)

Este blueprint define el estándar para la creación de mappers en JOIN CRM. Es obligatorio el uso de **Riok.Mapperly** (Source Generators) para garantizar el mejor rendimiento y evitar el overhead de reflexión en tiempo de ejecución.

## 1. Definición del Mapper (Capa: JOIN.Application)

### Reglas de Oro
1. **Definición**: Los mappers deben ser `partial interface` y estar decorados con el atributo `[Mapper]`.
2. **Nombramiento**: Seguir el patrón `I{Entity}Mapper` (ej: `ICustomerMapper`).
3. **Ubicación**: Deben residir en el namespace de la entidad o del caso de uso correspondiente dentro de `JOIN.Application`.
4. **Ignorar Propiedades**: Usar `[MapperIgnoreSource]` o `[MapperIgnoreTarget]` para campos auditables o IDs generados por la BD.

### Ejemplo de Mapper Estándar
```csharp
using Riok.Mapperly.Abstractions;
using JOIN.Domain.Admin;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.Customers.Commands;

namespace JOIN.Application.Mappings;

/// <summary>
/// High-performance mapper for Customer entity transformations.
/// </summary>
[Mapper]
public partial interface ICustomerMapper
{
    /// <summary>
    /// Maps a Create command to a Domain Entity, ignoring the auto-generated Id.
    /// </summary>
    [MapperIgnoreTarget(nameof(Customer.Id))]
    [MapperIgnoreTarget(nameof(Customer.Created))]
    [MapperIgnoreTarget(nameof(Customer.GcRecord))]
    public partial Customer ToEntity(CreateCustomerCommand source);

    /// <summary>
    /// Maps a Domain Entity to a Data Transfer Object.
    /// </summary>
    public partial CustomerDto ToDto(Customer source);
}
´´

2. Uso en el Handler
El mapper debe inyectarse vía Primary Constructor en el Handler del Command.

C#
public class CreateCustomerHandler(IUnitOfWork unitOfWork, ICustomerMapper mapper) 
    : IRequestHandler<CreateCustomerCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(...)
    {
        // Uso del mapper generado
        var entity = mapper.ToEntity(request);
        // ...
    }
}
´´´

3. Checklist de Revisión
[ ] ¿Es una partial interface?
[ ] ¿Tiene el atributo [Mapper]?
[ ] ¿Ignora los campos de auditoría (Created, GcRecord) al mapear hacia la Entidad?
[ ] ¿Los nombres de los métodos son claros (ToEntity, ToDto)?
[ ] ¿Tiene XML Comments en inglés?