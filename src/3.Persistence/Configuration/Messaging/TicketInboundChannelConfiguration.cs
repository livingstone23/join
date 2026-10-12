// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Messaging;

/// <summary>
/// Persistence rules for <see cref="TicketInboundChannel"/> (SPEC 47 F1).
/// <para>
/// Two filtered unique indexes:
/// <list type="bullet">
/// <item><c>WebhookToken</c> — global, so an anonymous webhook URL can resolve the
/// inbound channel regardless of tenant context (the only place in the system
/// where a value is unique across tenants on a tenant-scoped entity).</item>
/// <item><c>(CompanyId, ChannelId)</c> — at most one active inbound endpoint per
/// tenant per channel; soft-delete + create to rotate a token.</item>
/// </list>
/// Both indexes use <c>HasFilter("\"gcrecord\" = 0")</c> — PostgreSQL identifier casing
/// is preserved verbatim so a soft-deleted row never blocks a fresh insert.
/// </para>
/// </summary>
public sealed class TicketInboundChannelConfiguration : IEntityTypeConfiguration<TicketInboundChannel>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TicketInboundChannel> builder)
    {
        builder.ToTable("TicketInboundChannels", "Messaging");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.WebhookToken)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        // The lookup key for anonymous webhook URLs. Global uniqueness is intentional:
        // the controller has no CompanyId context until the token resolves.
        builder.HasIndex(x => x.WebhookToken)
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_TicketInboundChannels_Token_Active");

        // At most one active inbound endpoint per (company, channel).
        builder.HasIndex(x => new { x.CompanyId, x.ChannelId })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_TicketInboundChannels_Company_Channel_Active");

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Channel)
            .WithMany()
            .HasForeignKey(x => x.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
