using System;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Represents the tenant default configuration used when creating tickets.
/// </summary>
public record TicketCompanyDefaultDto
{
    /// <summary>
    /// Gets the configuration identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the tenant company identifier.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets the tenant company display name.
    /// </summary>
    public string? CompanyName { get; init; }

    /// <summary>
    /// Gets the logical delete marker value.
    /// </summary>
    public int GcRecord { get; init; }

    /// <summary>
    /// Gets whether the configuration has been logically deleted.
    /// </summary>
    public bool IsDeleted => GcRecord != 0;

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
    /// Gets the default status identifier.
    /// </summary>
    public Guid? TicketStatusDefaultId { get; init; }

    /// <summary>
    /// Gets the default status display name.
    /// </summary>
    public string? StatusName { get; init; }

    /// <summary>
    /// Gets the default complexity identifier.
    /// </summary>
    public Guid? TicketComplexityDefaultId { get; init; }

    /// <summary>
    /// Gets the default complexity display name.
    /// </summary>
    public string? ComplexityName { get; init; }

    /// <summary>
    /// Gets the default time unit identifier.
    /// </summary>
    public Guid? TimeUnitDefaultId { get; init; }

    /// <summary>
    /// Gets the default time unit display name.
    /// </summary>
    public string? TimeUnitName { get; init; }

    /// <summary>
    /// Gets the default area identifier.
    /// </summary>
    public Guid? AreaDefaultId { get; init; }

    /// <summary>
    /// Gets the default area display name.
    /// </summary>
    public string? AreaName { get; init; }

    /// <summary>
    /// Gets the default project identifier.
    /// </summary>
    public Guid? ProjectDefaultId { get; init; }

    /// <summary>
    /// Gets the default project display name.
    /// </summary>
    public string? ProjectName { get; init; }

    /// <summary>
    /// Gets the default communication channel identifier.
    /// </summary>
    public Guid? ChannelDefaultId { get; init; }

    /// <summary>
    /// Gets the default communication channel display name.
    /// </summary>
    public string? ChannelName { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
