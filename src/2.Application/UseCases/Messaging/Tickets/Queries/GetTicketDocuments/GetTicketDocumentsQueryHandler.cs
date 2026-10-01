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

        // Note: the CreatedByUserId projection relies on the audit interceptor
        // storing CreatedBy as the user Guid (it does — see AuditableEntitySaveChangesInterceptor).
        // JOIN matches it against Security.Users.Id cast from nvarchar.
        const string sql = """
            SELECT
                td.Id,
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
            LEFT JOIN Security.Users u ON u.Id = CAST(td.CreatedBy AS UNIQUEIDENTIFIER)
            WHERE td.TicketId = @TicketId
              AND td.CompanyId = @TenantId
              AND td.GcRecord = 0
            ORDER BY td.Created DESC;
            """;

        var items = (await connection.QueryAsync<TicketDocumentDto>(
            new CommandDefinition(
                sql,
                new { request.TicketId, TenantId = currentUserService.CompanyId },
                cancellationToken: cancellationToken))).AsList();

        return new Response<IReadOnlyCollection<TicketDocumentDto>>
        {
            IsSuccess = true,
            Message = "Ticket documents retrieved successfully.",
            Data = items
        };
    }
}