using Dapper;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries.DownloadTicketDocument;

/// <summary>
/// Maneja la descarga de un documento adjunto:
///   1. Carga la fila de metadata validando <c>TicketId</c> + <c>CompanyId</c>.
///   2. Si la fila existe, abre el stream vía <see cref="IFileStorageService"/>.
///   3. Devuelve <see cref="TicketDocumentDownloadResult"/> para que el
///      controller la traduzca a <c>File(stream, contentType, fileName)</c>.
///
/// Inconsistencias (fila en base sin archivo en disco) se traducen a
/// <c>FILE_NOT_FOUND_IN_STORAGE</c> (404), no a 500 — son recuperables por
/// ops si deciden rehidratar el binario desde un backup.
/// </summary>
public sealed class DownloadTicketDocumentQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    IFileStorageService fileStorageService)
    : IRequestHandler<DownloadTicketDocumentQuery, Response<TicketDocumentDownloadResult>>
{
    public async Task<Response<TicketDocumentDownloadResult>> Handle(DownloadTicketDocumentQuery request, CancellationToken cancellationToken)
    {
        var tenantId = currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<TicketDocumentDownloadResult>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        // Only metadata — the binary is streamed by IFileStorageService below.
        const string sql = """
            SELECT
                td.Id,
                td.TicketId,
                td.CompanyId,
                td.Path,
                td.ContentType,
                td.OriginalName
            FROM Support.TicketDocuments td
            WHERE td.Id = @Id
              AND td.TicketId = @TicketId
              AND td.CompanyId = @TenantId
              AND td.GcRecord = 0;
            """;

        var row = await connection.QuerySingleOrDefaultAsync<TicketDocumentDownloadRow>(
            new CommandDefinition(
                sql,
                new { request.Id, request.TicketId, TenantId = tenantId },
                cancellationToken: cancellationToken));

        if (row is null)
        {
            return Response<TicketDocumentDownloadResult>.Error("TICKET_DOCUMENT_NOT_FOUND", ["Ticket document not found for the current ticket and tenant."]);
        }

        var stream = await fileStorageService.OpenReadAsync(row.Path, cancellationToken);
        if (stream is null)
        {
            return Response<TicketDocumentDownloadResult>.Error("FILE_NOT_FOUND_IN_STORAGE", ["The underlying file is missing from storage."]);
        }

        return new Response<TicketDocumentDownloadResult>
        {
            IsSuccess = true,
            Message = "Ticket document retrieved successfully.",
            Data = new TicketDocumentDownloadResult(stream, row.ContentType, row.OriginalName)
        };
    }

    /// <summary>
    /// Projection interna de la fila de metadata necesaria para abrir el stream.
    /// </summary>
    private sealed class TicketDocumentDownloadRow
    {
        public Guid Id { get; init; }
        public Guid TicketId { get; init; }
        public Guid CompanyId { get; init; }
        public string Path { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public string OriginalName { get; init; } = string.Empty;
    }
}