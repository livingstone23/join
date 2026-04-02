# Blueprint: Entity Framework Configuration (Fluent API)
This blueprint defines the standard for persistence configurations within JOIN.Persistence. It utilizes Fluent API to decouple database-specific concerns from the core domain model.

## 1. Configuration Rules

1. **Esquemas**: It is mandatory to explicitly define the table schema (e.g., Admin, Common, Security, Support).
2. **Naming**: Table names must be pluralized.
3. **Primary Keys**: Always configure the primary key using .HasKey(x => x.Id).
4. **Relationships**: Explicitly define relationships and their specific deletion behavior (OnDelete).
5. **Multi-Tenancy**: filters are applied globally at the DbContext level; do not duplicate this logic within individual configurations.aquí.

## 2. Standard Configuration Example

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

3. Review Checklist
[ ] Does it explicitly define the Schema?
[ ] Is the table name pluralized?
[ ] Does it implement IEntityTypeConfiguration<T>?
[ ] Are MaxLength constraints configured for all string properties?
[ ] Does it include English XML comments for the class?
