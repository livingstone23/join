using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.AddTicketNote;

/// <summary>
/// Appends a note (<see cref="LogType.InternalNote"/> or <see cref="LogType.ExternalNote"/>)
/// to a ticket. Returns the freshly-created <see cref="TicketLogDto"/> rather than the
/// full ticket projection — the caller already has the ticket open.
/// </summary>
public record AddTicketNoteCommand : ITransactionalCommand<Response<TicketLogDto>>
{
    /// <summary>
    /// Gets the ticket identifier the note belongs to.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; init; }

    /// <summary>
    /// Gets the note type.
    /// </summary>
    public LogType LogType { get; init; }

    /// <summary>
    /// Gets the note summary.
    /// </summary>
    public string Summary { get; init; } = string.Empty;
}
