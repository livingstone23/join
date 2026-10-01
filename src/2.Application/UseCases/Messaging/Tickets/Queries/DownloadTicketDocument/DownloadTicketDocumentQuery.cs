using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries.DownloadTicketDocument;

/// <summary>
/// Query para abrir el stream de un documento adjunto y devolver
/// <see cref="TicketDocumentDownloadResult"/> con el contenido, el
/// <c>Content-Type</c> y el nombre original. El controller es dueño del
/// ciclo de vida del <see cref="System.IO.Stream"/> devuelto (lo pasa a
/// <c>File(stream, contentType, fileName)</c> que ASP.NET Core dispone al
/// terminar la respuesta).
/// </summary>
/// <param name="TicketId">Identificador del ticket al que pertenece el documento.</param>
/// <param name="Id">Identificador del documento a descargar.</param>
public record DownloadTicketDocumentQuery(Guid TicketId, Guid Id) : IRequest<Response<TicketDocumentDownloadResult>>;

/// <summary>
/// Resultado de <see cref="DownloadTicketDocumentQueryHandler"/>. El
/// controller traduce los tres campos a una respuesta <c>File(...)</c>.
/// </summary>
/// <param name="Content">Stream del archivo. El controller es dueño de su ciclo de vida.</param>
/// <param name="ContentType">MIME type (ej. "application/pdf").</param>
/// <param name="OriginalName">Nombre original del archivo, para el header
/// <c>Content-Disposition</c>.</param>
public sealed record TicketDocumentDownloadResult(
    System.IO.Stream Content,
    string ContentType,
    string OriginalName);