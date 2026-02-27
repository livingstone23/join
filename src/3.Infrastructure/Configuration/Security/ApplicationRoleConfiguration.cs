// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Infrastructure.Configuration.Security;

/// <summary>
/// Configures the database mapping for the <see cref="ApplicationRole"/> entity.
/// </summary>
public class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    /// <summary>
    /// Configures the <see cref="ApplicationRole"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        // Maps the entity to the "Roles" table in the "Security" schema.
        builder.ToTable("Roles", "Security");

        builder.Property(r => r.Description).HasMaxLength(256);
    }
}
