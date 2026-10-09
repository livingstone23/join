using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries.GetTicketDocuments;

/// <summary>
/// Maneja el listado de documentos adjuntos de un ticket vía Dapper. Solo
/// trae filas activas (<c>GcRecord = 0</c>), ordenadas por
/// <c>Created DESC</c>, scoped por <c>TicketId</c> + <c>CompanyId</c>.
/// </summary>
public sealed class GetTicketDocumentsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTicketDocumentsQuery, Response<IReadOnlyCollection<TicketDocumentDto>>>
{
    public async Task<Response<IReadOnlyCollection<TicketDocumentDto>>> Handle(GetTicketDocumentsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyCollection<TicketDocumentDto>>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        // CreatedBy is the user Guid written by AuditableEntitySaveChangesInterceptor,
        // but it falls back to "System" when there is no authenticated user (e.g. the
        // anonymous inbound-channel webhooks). Compare as strings so such rows simply
        // get no user instead of failing the whole query on a GUID cast; CONCAT keeps
        // it portable across SQL Server and Postgres.
        // SPEC 41: deleted rows only for a SuperAdmin asking includeDeleted=true.
        var activeOnly = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted)
            ? string.Empty
            : "AND td.GcRecord = 0";

        var sql = $"""
            SELECT
                td.Id,
                td.GcRecord,
                td.TicketId,
                td.TicketLogsId,
                CASE td.DocumentType
                    WHEN 1 THEN 'Pdf'
                    WHEN 2 THEN 'Word'
                    WHEN 4 THEN 'Excel'
                    WHEN 8 THEN 'Text'
                    WHEN 16 THEN 'Image'
                    WHEN 32 THEN 'Other'
                    ELSE CONCAT('Unknown(', td.DocumentType, ')')
                END AS DocumentType,
                td.OriginalName,
                td.ContentType,
                td.SizeBytes,
                u.Id AS CreatedByUserId,
                CONCAT(u.FirstName, ' ', u.LastName) AS CreatedByUserName,
                td.Created AS CreatedAt
            FROM Support.TicketDocuments td
            LEFT JOIN Security.Users u ON CONCAT(u.Id, '') = td.CreatedBy
            WHERE td.TicketId = @TicketId
              AND td.CompanyId = @TenantId
              {activeOnly}
            ORDER BY td.Created DESC;
            """;

        var items = (await connection.QueryAsync<TicketDocumentDto>(
            new CommandDefinition(
                sql,
                new { request.TicketId, TenantId = TenantResolver.Resolve(currentUserService, request.CompanyId) },
                cancellationToken: cancellationToken))).AsList();

        return new Response<IReadOnlyCollection<TicketDocumentDto>>
        {
            IsSuccess = true,
            Message = "Ticket documents retrieved successfully.",
            Data = items
        };
    }
}