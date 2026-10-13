using Dapper;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries.DownloadTicketDocument;

/// <summary>
/// Maneja la descarga de un documento adjunto:
///   1. Carga la fila de metadata validando <c>TicketId</c> + <c>CompanyId</c>.
///   2. Lee la columna <c>StorageProvider</c> de la fila y resuelve el
///      adapter que guardó el archivo (no el que está activo en el entorno:
///      un adjunto de Local debe seguir saliendo de Local aunque la API
///      ya esté en Azure; ver SPEC 49-post).
///   3. Si el adapter no está configurado en este entorno, devuelve
///      <c>STORAGE_PROVIDER_NOT_CONFIGURED</c> (503). Si el adapter existe
///      pero el objeto no, <c>FILE_NOT_FOUND_IN_STORAGE</c> (404).
///   4. Devuelve <see cref="TicketDocumentDownloadResult"/> para que el
///      controller la traduzca a <c>File(stream, contentType, fileName)</c>.
/// </summary>
public sealed class DownloadTicketDocumentQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    IFileStorageResolver fileStorageResolver)
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
        // SPEC 49-post: td.StorageProvider travels with the row so the download
        // opens the file from the same provider that uploaded it.
        const string sql = """
            SELECT
                td.Id,
                td.TicketId,
                td.CompanyId,
                td.Path,
                td.StorageProvider,
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

        var provider = fileStorageResolver.Resolve(row.StorageProvider);
        if (provider is null)
        {
            // Section removed from config (or the row was written by a
            // provider that was never deployed here) — recoverable from
            // ops by restoring the section, not a generic 500.
            return Response<TicketDocumentDownloadResult>.Error(
                "STORAGE_PROVIDER_NOT_CONFIGURED",
                [$"The storage provider '{row.StorageProvider}' required by this document is not configured in the current environment."]);
        }

        var stream = await provider.OpenReadAsync(row.Path, cancellationToken);
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
        public StorageProviderKind StorageProvider { get; init; }
        public string ContentType { get; init; } = string.Empty;
        public string OriginalName { get; init; } = string.Empty;
    }
}
