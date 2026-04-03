using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.CommunicationChannels.Commands;

/// <summary>
/// Command to update an existing communication channel.
/// </summary>
public record UpdateCommunicationChannelCommand : IRequest<Response<CommunicationChannelDto>>
{
    /// <summary>
    /// Gets the communication channel identifier to update.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the channel name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel provider.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Gets or sets the channel code.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// Gets or sets whether the channel is active.
    /// </summary>
    public bool IsActive { get; init; } = true;
}
