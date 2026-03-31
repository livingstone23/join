### 5. `persistence-configuration.md`
Asegura que las configuraciones de base de datos sigan el estándar de Fluent API.

```markdown
# Blueprint: Entity Framework Configuration
Usa esta estructura en `JOIN.Persistence` para configurar tablas.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using JOIN.Domain.Admin;

namespace JOIN.Persistence.Configurations.Admin;

public class SedeConfiguration : IEntityTypeConfiguration<Sede>
{
    public void Configure(EntityTypeBuilder<Sede> builder)
    {
        builder.ToTable("Sedes", "Admin");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
        
        // El filtro de CompanyId se maneja globalmente en el Context
    }
}
