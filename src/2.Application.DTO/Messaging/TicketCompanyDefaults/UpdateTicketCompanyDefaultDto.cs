using System;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Represents the payload used to update a tenant ticket default configuration.
/// </summary>
public record UpdateTicketCompanyDefaultDto
{
    /// <summary>
    /// Gets the ticket code prefix.
    /// </summary>
    public string StartCode { get; init; } = string.Empty;

    /// <summary>
    /// Gets the ticket code numeric sequence length.
    /// </summary>
    public int CodeSequenceLength { get; init; }

    /// <summary>
    /// Gets whether the personalized code format is enabled.
    /// </summary>
    public bool UsePersonalizedCode { get; init; }

    /// <summary>
    /// Gets the optional default status identifier.
    /// </summary>
    public Guid? TicketStatusDefaultId { get; init; }

    /// <summary>
    /// Gets the optional default complexity identifier.
    /// </summary>
    public Guid? TicketComplexityDefaultId { get; init; }

    /// <summary>
    /// Gets the optional default time unit identifier.
    /// </summary>
    public Guid? TimeUnitDefaultId { get; init; }

    /// <summary>
    /// Gets the optional default area identifier.
    /// </summary>
    public Guid? AreaDefaultId { get; init; }

    /// <summary>
    /// Gets the optional default project identifier.
    /// </summary>
    public Guid? ProjectDefaultId { get; init; }

    /// <summary>
    /// Gets the optional default channel identifier.
    /// </summary>
    public Guid? ChannelDefaultId { get; init; }
}
