# Blueprint: Entity Framework Configuration (Fluent API)

Este blueprint define el estándar para las configuraciones de persistencia en `JOIN.Persistence`. Se utiliza **Fluent API** para separar las preocupaciones de la base de datos del modelo de dominio.

## 1. Reglas de Configuración

1. **Esquemas**: Es obligatorio definir el esquema de la tabla (ej: `Admin`, `Common`, `Security`, `Support`).
2. **Nombramiento**: Las tablas deben nombrarse en plural.
3. **Llaves Primarias**: Configurar siempre `.HasKey(x => x.Id)`.
4. **Relaciones**: Definir explícitamente las relaciones y el comportamiento de borrado (`OnDelete`).
5. **Multi-Tenancy**: Los filtros de `CompanyId` se aplican de forma global en el `DbContext`. No duplicar esa lógica aquí.

## 2. Ejemplo de Configuración Estándar

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using JOIN.Domain.Admin;

namespace JOIN.Persistence.Configurations.Admin;

/// <summary>
/// Database configuration for the Customer entity.
/// </summary>
public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        // 1. Table and Schema mapping
        builder.ToTable("Customers", "Admin");

        // 2. Primary Key
        builder.HasKey(x => x.Id);

        // 3. Property Constraints
        builder.Property(x => x.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.IdentificationNumber)
            .IsRequired()
            .HasMaxLength(50);

        // 4. Relationships
        builder.HasOne(x => x.IdentificationType)
            .WithMany()
            .HasForeignKey(x => x.IdentificationTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // 5. Indexes for Performance
        builder.HasIndex(x => new { x.CompanyId, x.IdentificationNumber })
            .IsUnique()
            .HasDatabaseName("IX_Customer_Company_Identification");
    }
}
´´´

3. Checklist de Revisión
[ ] ¿Define explícitamente el esquema (Schema)?
[ ] ¿El nombre de la tabla está en plural?
[ ] ¿Usa IEntityTypeConfiguration<T>?
[ ] ¿Configura los límites de longitud (HasMaxLength) para strings?
[ ] ¿Tiene comentarios XML en inglés?
