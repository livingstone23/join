using System.Text.Json.Serialization;
using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.DeleteTicketDocument;

/// <summary>
/// Command para el borrado lógico de un documento adjunto.
/// El handler NO borra el archivo físico — la metadata se marca como
/// eliminada para que desaparezca del listado, pero el binario se
/// conserva como evidencia del ciclo de vida del ticket.
/// </summary>
public record DeleteTicketDocumentCommand : ITransactionalCommand<Response<Guid>>
{
    /// <summary>
    /// Identificador del ticket al que pertenece el documento.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; init; }

    /// <summary>
    /// Identificador del documento a eliminar.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; init; }
}