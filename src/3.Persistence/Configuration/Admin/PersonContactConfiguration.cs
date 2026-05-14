// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Admin;



/// <summary>
/// Configures the database mapping for the <see cref="PersonContact"/> entity.
/// Defines table structure and constraints for customer contact methods.
/// </summary>
public class PersonContactConfiguration : IEntityTypeConfiguration<PersonContact>
{
    /// <summary>
    /// Configures the <see cref="PersonContact"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<PersonContact> builder)
    {
        // Map to table "PersonContacts" in schema "Admin"
        builder.ToTable("PersonContacts", "Admin");

        // Set the primary key
        builder.HasKey(cc => cc.Id);

        // --- Properties ---


        // ContactType is an enum, configured to be stored as an integer.
        builder.Property(c => c.ContactType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(cc => cc.ContactValue).IsRequired().HasMaxLength(150);
        builder.Property(cc => cc.IsPrimary).IsRequired();
        builder.Property(cc => cc.Comments).HasMaxLength(500);

        // --- Relationships ---

        // Required relationship with Person
        builder.HasOne(cc => cc.Person)
            .WithMany(c => c.Contacts)
            .HasForeignKey(cc => cc.PersonId)
            .OnDelete(DeleteBehavior.Restrict); // Contacts are deleted if the Person is deleted.

        // --- Query Filters ---

        // Apply a soft-delete filter.
        builder.HasQueryFilter(cc => cc.GcRecord == 0);
    }
}
