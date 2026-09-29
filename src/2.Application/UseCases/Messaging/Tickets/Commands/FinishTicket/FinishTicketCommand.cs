using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.FinishTicket;

/// <summary>
/// Finalizes a ticket by transitioning it to a status with <c>IsFinal = true</c>.
/// Records a <c>Finalization</c> log entry (SPEC 35) with the supplied resolution summary.
/// </summary>
public record FinishTicketCommand : ITransactionalCommand<Response<TicketDto>>
{
    /// <summary>
    /// Gets the ticket identifier to finalize.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; init; }

    /// <summary>
    /// Gets the target final status identifier.
    /// </summary>
    public Guid TicketStatusId { get; init; }

    /// <summary>
    /// Gets the optional resolution summary to record on the finalization log.
    /// </summary>
    public string? ResolutionSummary { get; init; }
}
