namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// WebApi-facing DTO for appending an internal or external note to a ticket via
/// <c>POST /api/v1/Tickets/{id}/notes</c>. Visibility is derived server-side from the
/// <c>LogType</c>: <c>2</c> (InternalNote) is hidden from third viewers, <c>3</c>
/// (ExternalNote) is public. The numeric values are exposed to keep the DTO layer
/// free of domain references; the controller casts to <c>JOIN.Domain.Enums.LogType</c>
/// before dispatching the command.
/// </summary>
public record AddTicketNoteDto
{
    /// <summary>
    /// Gets the note type as the underlying enum integer. Only <c>2</c> (InternalNote)
    /// and <c>3</c> (ExternalNote) are accepted; any other value fails validation.
    /// </summary>
    public int LogType { get; init; }

    /// <summary>
    /// Gets the human-readable note summary.
    /// </summary>
    public string Summary { get; init; } = string.Empty;
}
