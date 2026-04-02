# Blueprint: Domain Entity (DDD & Multi-Tenancy)

Este blueprint define el estándar para la creación de entidades de dominio en `JOIN.Domain`. Las entidades deben ser representaciones puras del negocio y seguir las reglas de herencia para auditoría y aislamiento de datos.

## 1. Opciones de Herencia

### BaseTenantEntity
- **Uso**: Para datos que pertenecen a una empresa específica (ej: Clientes, Proyectos, Sedes).
- **Efecto**: Incluye `CompanyId` y es filtrado automáticamente por el Tenant Filter.

### BaseAuditableEntity
- **Uso**: Para datos globales o compartidos que requieren trazabilidad (ej: Países, Monedas).
- **Efecto**: Incluye campos de auditoría (`Created`, `LastModified`) pero NO `CompanyId`.

### BaseEntity
- **Uso**: Para tablas maestras muy simples o de sistema sin necesidad de auditoría extendida.

## 2. Reglas de Codificación

1. **Constructores**: **PROHIBIDO** usar *Primary Constructors* en entidades de dominio. EF Core requiere un constructor sin parámetros (puede ser protegido) para la materialización y creación de proxies.
2. **Propiedades**: Usar `public virtual` para propiedades de navegación para permitir *Lazy Loading* si fuera necesario.
3. **Colecciones**: Inicializar colecciones en el constructor o como propiedades auto-implementadas para evitar `NullReferenceException`.

## 3. Ejemplo de Entidad Estándar

```csharp
using JOIN.Domain.Common;

namespace JOIN.Domain.Admin;

/// <summary>
/// Represents a Customer within a specific company context.
/// </summary>
public class Customer : BaseTenantEntity
{
    /// <summary>
    /// Gets or sets the first name of the customer.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the associated identification type.
    /// </summary>
    public Guid IdentificationTypeId { get; set; }

    // Navigation Properties
    public virtual IdentificationType IdentificationType { get; set; } = null!;
    public virtual ICollection<CustomerAddress> Addresses { get; set; } = [];

    /// <summary>
    /// Standard constructor for EF Core materialization.
    /// </summary>
    public Customer() 
    { 
        Addresses = new HashSet<CustomerAddress>();
    }
}
´´

4. Checklist de Revisión
[ ] ¿Hereda de la clase base correcta (BaseTenantEntity o BaseAuditableEntity)?
[ ] ¿Evita el uso de Primary Constructors?
[ ] ¿Tiene comentarios XML en inglés para cada propiedad?
[ ] ¿Las propiedades de navegación son virtual?
[ ] ¿Las colecciones están inicializadas?