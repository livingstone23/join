using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries.GetTicketDocuments;

/// <summary>
/// Query para obtener la lista plana de documentos adjuntos activos de un
/// ticket, ordenada por <c>Created DESC</c>. Acotada por
/// <c>MaxFilesPerTicket</c> de la configuración del tenant.
/// </summary>
/// <param name="TicketId">Identificador del ticket.</param>
public record GetTicketDocumentsQuery(Guid TicketId) : IRequest<Response<IReadOnlyCollection<TicketDocumentDto>>>;