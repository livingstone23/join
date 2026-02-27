// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Infrastructure.Configuration.Admin;



/// <summary>
/// Configures the database mapping for the <see cref="CustomerContact"/> entity.
/// Defines table structure and constraints for customer contact methods.
/// </summary>
public class CustomerContactConfiguration : IEntityTypeConfiguration<CustomerContact>
{
    /// <summary>
    /// Configures the <see cref="CustomerContact"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<CustomerContact> builder)
    {
        // Map to table "CustomerContacts" in schema "Admin"
        builder.ToTable("CustomerContacts", "Admin");

        // Set the primary key
        builder.HasKey(cc => cc.Id);

        // --- Properties ---


        // ContactType is an enum, configured to be stored as a string.
        builder.Property(c => c.ContactType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(cc => cc.ContactValue).IsRequired().HasMaxLength(150);
        builder.Property(cc => cc.IsPrimary).IsRequired();
        builder.Property(cc => cc.Comments).HasMaxLength(500);

        // --- Relationships ---

        // Required relationship with Customer
        builder.HasOne(cc => cc.Customer)
            .WithMany(c => c.Contacts)
            .HasForeignKey(cc => cc.CustomerId)
            .OnDelete(DeleteBehavior.Cascade); // Contacts are deleted if the Customer is deleted.

        // --- Query Filters ---

        // Apply a soft-delete filter.
        builder.HasQueryFilter(cc => cc.GcRecord == 0);
    }
}
