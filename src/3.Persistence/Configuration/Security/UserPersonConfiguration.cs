// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Security;



/// <summary>
/// Configures the database mapping for the <see cref="UserPerson"/> entity.
/// Secures the B2B/B2C portal architecture by defining how external users are linked to CRM customers.
/// </summary>
public class UserPersonConfiguration : IEntityTypeConfiguration<UserPerson>
{
    /// <summary>
    /// Applies the configuration rules using the provided builder.
    /// </summary>
    public void Configure(EntityTypeBuilder<UserPerson> builder)
    {
        // 1. Table & Schema Mapping
        // Placed in the "Security" schema because this is an access control intersection,
        // even though it references an "Admin" or "CRM" domain entity (Person).
        builder.ToTable("UserPersons", "Security");

        // 2. Primary Key
        builder.HasKey(uc => uc.Id);

        // 3. Business Rules & Indexes
        // Composite Unique Index: Prevents linking the same identity user to the same customer CRM record more than once.
        builder.HasIndex(uc => new { uc.UserId, uc.PersonId })
            .IsUnique();

        // 4. Relationships (Foreign Keys & Delete Behaviors)

        // Relationship with ApplicationUser (The login account)
        builder.HasOne(uc => uc.User)
            .WithMany() // Add u => u.UserPersons here ONLY if you add the ICollection to ApplicationUser
            .HasForeignKey(uc => uc.UserId)
            .OnDelete(DeleteBehavior.Cascade); // If the user account is hard-deleted, their access link is destroyed.

        // Relationship with Person (The business CRM record)
        builder.HasOne(uc => uc.Person)
            .WithMany() // Add c => c.UserPersons here ONLY if you add the ICollection to Person
            .HasForeignKey(uc => uc.PersonId)
            .OnDelete(DeleteBehavior.Restrict); // CRITICAL: Protect the CRM business record from being deleted by accident if linked to a user.

        // 5. Global Query Filters
        // Automatically excludes soft-deleted portal access records from queries.
        builder.HasQueryFilter(uc => uc.GcRecord == 0);
    }
}