using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Queries;

/// <summary>
/// Maneja el listado tenant-scoped de configuraciones de adjuntos vía Dapper.
/// Lista plana sin paginar — el dominio es singleton por empresa (0 o 1 fila).
/// </summary>
public sealed class GetTicketAttachmentSettingsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTicketAttachmentSettingsQuery, Response<IReadOnlyCollection<TicketAttachmentSettingsDto>>>
{
    /// <summary>
    /// Recupera la fila activa (si existe) de la empresa actual.
    /// </summary>
    public async Task<Response<IReadOnlyCollection<TicketAttachmentSettingsDto>>> Handle(GetTicketAttachmentSettingsQuery request, CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyCollection<TicketAttachmentSettingsDto>>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        // SPEC 41: deleted rows only for a SuperAdmin asking includeDeleted=true.
        var activeOnly = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted)
            ? string.Empty
            : "AND tas.GcRecord = 0";

        var sql = $"""
            SELECT
                tas.Id,
                tas.CompanyId,
                c.Name AS CompanyName,
                tas.GcRecord,
                tas.AllowedDocumentTypes,
                tas.MaxFileSizeBytes,
                tas.MaxFilesPerTicket,
                tas.MaxFilesPerDay,
                tas.Created AS CreatedAt
            FROM Messaging.TicketAttachmentSettings tas
            LEFT JOIN Common.Companies c ON c.Id = tas.CompanyId
            WHERE tas.CompanyId = @TenantId
              {activeOnly}
            ORDER BY tas.Created DESC;
            """;

        var items = (await connection.QueryAsync<TicketAttachmentSettingsDto>(
            new CommandDefinition(sql, new { TenantId = TenantResolver.Resolve(currentUserService, request.CompanyId) }, cancellationToken: cancellationToken))).AsList();

        return new Response<IReadOnlyCollection<TicketAttachmentSettingsDto>>
        {
            IsSuccess = true,
            Message = "Ticket attachment settings retrieved successfully.",
            Data = items
        };
    }
}