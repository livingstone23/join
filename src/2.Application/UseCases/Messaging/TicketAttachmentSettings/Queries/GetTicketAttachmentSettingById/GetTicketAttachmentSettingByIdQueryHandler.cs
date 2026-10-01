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

        const string sql = """
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
              AND tas.GcRecord = 0;
            """;

        var item = await connection.QuerySingleOrDefaultAsync<TicketAttachmentSettingsDto>(
            new CommandDefinition(sql, new { request.Id, TenantId = currentUserService.CompanyId }, cancellationToken: cancellationToken));

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