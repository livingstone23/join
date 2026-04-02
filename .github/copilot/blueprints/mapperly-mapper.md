# Blueprint: Riok.Mapperly Mapper (High-Performance Mapping)

This blueprint defines the standard for creating mappers within JOIN CRM. The use of Riok.Mapperly (Source Generators) is mandatory to ensure peak performance and eliminate runtime reflection overhead.

## 1. Mapper Definition (Layer: JOIN.Application)

### Golden Rules
Definition: Mappers must be a partial interface and decorated with the [Mapper] attribute.
Naming: Follow the I{Entity}Mapper pattern (e.g., ICustomerMapper).
Location: They must reside in the entity's namespace or the corresponding use case namespace within JOIN.Application.
Ignore Properties: Use [MapperIgnoreSource] or [MapperIgnoreTarget] for audit fields or database-generated IDs.

### Standard Mapper Example

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

2. Usage in the Handler
The mapper must be injected via a Primary Constructor in the Command Handler.

```csharp
public class CreateCustomerHandler(IUnitOfWork unitOfWork, ICustomerMapper mapper) 
    : IRequestHandler<CreateCustomerCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(...)
    {
        // Usage of the generated mapper
        var entity = mapper.ToEntity(request);
        // ...
    }
}
´´

3. Review Checklist
[ ] Is it a partial interface?
[ ] Does it have the [Mapper] attribute?
[ ] Does it ignore audit fields (Created, GcRecord) when mapping toward the Entity?
[ ] Are the method names clear (ToEntity, ToDto)?
[ ] Does it include XML Comments in English?
