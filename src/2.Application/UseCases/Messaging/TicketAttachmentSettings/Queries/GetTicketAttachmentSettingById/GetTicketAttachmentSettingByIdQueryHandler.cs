using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Queries;

/// <summary>
/// Maneja la query de detalle de configuración de adjuntos vía Dapper.
/// </summary>
public sealed class GetTicketAttachmentSettingByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTicketAttachmentSettingByIdQuery, Response<TicketAttachmentSettingsDto>>
{
    /// <summary>
    /// Recupera la configuración por id, validando pertenencia al tenant.
    /// </summary>
    public async Task<Response<TicketAttachmentSettingsDto>> Handle(GetTicketAttachmentSettingByIdQuery request, CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketAttachmentSettingsDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
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
            WHERE tas.Id = @Id
              AND tas.CompanyId = @TenantId
              {activeOnly};
            """;

        var item = await connection.QuerySingleOrDefaultAsync<TicketAttachmentSettingsDto>(
            new CommandDefinition(sql, new { request.Id, TenantId = TenantResolver.Resolve(currentUserService, request.CompanyId) }, cancellationToken: cancellationToken));

        if (item is null)
        {
            return Response<TicketAttachmentSettingsDto>.Error("TICKET_ATTACHMENT_SETTINGS_NOT_FOUND", ["Ticket attachment settings configuration not found for the current tenant."]);
        }

        return new Response<TicketAttachmentSettingsDto>
        {
            IsSuccess = true,
            Message = "Ticket attachment settings retrieved successfully.",
            Data = item
        };
    }
}