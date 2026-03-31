### 3. `mapperly-mapper.md`
Obliga a la IA a usar **Riok.Mapperly** en lugar de otros mappers.

```markdown
# Blueprint: Riok.Mapperly Mapper
Usa esta estructura en `JOIN.Application` para definir mappers de alto rendimiento.

```csharp
using Riok.Mapperly.Abstractions;
using JOIN.Domain.Admin;
using JOIN.Application.DTO.Admin;

namespace JOIN.Application.UseCases.Admin;

[Mapper]
public partial interface ICustomerMapper
{
    public partial CustomerDto ToDto(Customer source);
    
    [MapperIgnoreSource(nameof(Customer.Id))]
    public partial Customer ToEntity(CreateCustomerRequest source);
}