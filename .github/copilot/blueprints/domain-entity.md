### 4. `domain-entity.md`
Define la diferencia entre entidades globales y entidades multi-tenant.

```markdown
# Blueprint: Domain Entity
Usa esta estructura para definir entidades en `JOIN.Domain`.

## Opciones de Herencia
- **BaseTenantEntity**: Para datos que pertenecen a una empresa (Clientes, Sedes).
- **BaseAuditableEntity**: Para datos globales que requieren auditoría (Países).
- **BaseEntity**: Para tablas maestras simples.

```csharp
namespace JOIN.Domain.Admin;

/// <summary>
/// Represents a Sede in the system
/// </summary>
public class Sede : BaseTenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    
    // Primary Constructor is not used in Entities to keep EF compatibility
    public Sede() { } 
}