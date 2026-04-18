// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Admin;



/// <summary>
/// Configures the database mapping for the <see cref="UserCommunicationChannel"/> entity.
/// This setup defines the join table linking users to their specific communication channels
/// and identifiers, such as a phone number for WhatsApp.
/// </summary>
public class UserCommunicationChannelConfiguration : IEntityTypeConfiguration<UserCommunicationChannel>
{
    /// <summary>
    /// Configures the <see cref="UserCommunicationChannel"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<UserCommunicationChannel> builder)
    {

        // Maps the entity to the "UserCommunicationChannels" table in the "Admin" schema.
        builder.ToTable("UserCommunicationChannels", "Admin");


        // Sets the primary key for the entity.
        builder.HasKey(p => p.Id);

        builder.Property(p => p.CompanyId)
            .IsRequired();

        // Configures the 'ChannelIdentifier' property: it is required and has a maximum length of 100 characters.
        builder.Property(p => p.ChannelIdentifier)
            .IsRequired()
            .HasMaxLength(100);

        // --- Relationships ---

        builder.HasOne(p => p.Company)
            .WithMany()
            .HasForeignKey(p => p.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Defines a one-to-many relationship with ApplicationUser.
        // A UserCommunicationChannel belongs to one User.
        builder.HasOne(p => p.User)
            .WithMany(u => u.Channels)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Defines a one-to-many relationship with CommunicationChannel.
        // A UserCommunicationChannel belongs to one Channel.
        builder.HasOne(p => p.Channel)
            .WithMany()
            .HasForeignKey(p => p.CommunicationChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Indexes ---

        // Creates a unique tenant-aware index to ensure a user can only be mapped
        // to the same communication channel once within a company.
        builder.HasIndex(p => new { p.CompanyId, p.UserId, p.CommunicationChannelId })
            .IsUnique()
            .HasFilter("[GcRecord] = 0");


        // Apply a soft-delete filter to automatically exclude records marked as deleted.
        builder.HasQueryFilter(a => a.GcRecord == 0);
        

    }
}
