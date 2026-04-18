using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;

/// <summary>
/// Command used to update the tenant ticket default configuration.
/// </summary>
public record UpdateTicketCompanyDefaultCommand : IRequest<Response<TicketCompanyDefaultDto>>
{
    /// <summary>
    /// Gets the configuration identifier.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the ticket code prefix.
    /// </summary>
    public string StartCode { get; init; } = string.Empty;

    /// <summary>
    /// Gets the numeric sequence length.
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
    /// Gets the optional default communication channel identifier.
    /// </summary>
    public Guid? ChannelDefaultId { get; init; }
}
