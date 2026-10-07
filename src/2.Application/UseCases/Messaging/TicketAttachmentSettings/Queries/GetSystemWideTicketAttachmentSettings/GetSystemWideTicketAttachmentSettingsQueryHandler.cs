using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Queries;

/// <summary>
/// Maneja la query cross-tenant (SuperAdmin) de configuraciones de adjuntos.
/// Mismo patrón de paginación portable (SQL Server / Postgres) que el resto
/// de los listados del módulo.
/// </summary>
public sealed class GetSystemWideTicketAttachmentSettingsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetSystemWideTicketAttachmentSettingsQuery, Response<PagedResult<TicketAttachmentSettingsDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Recupera la lista paginada cross-tenant con filtros opcionales.
    /// </summary>
    public async Task<Response<PagedResult<TicketAttachmentSettingsDto>>> Handle(GetSystemWideTicketAttachmentSettingsQuery request, CancellationToken cancellationToken)
    {
        var (sanitizedPageNumber, sanitizedPageSize) = _paginationSettings.Sanitize(request.PageNumber, request.PageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        using var connection = connectionFactory.CreateConnection();

        var parameters = new DynamicParameters();
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE 1 = 1");

        if (!string.IsNullOrWhiteSpace(request.CompanyName))
        {
            whereBuilder.Append(" AND c.Name LIKE @CompanyName");
            parameters.Add("CompanyName", $"%{request.CompanyName.Trim()}%");
        }

        var whereClause = whereBuilder.ToString();

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
            {whereClause}
            ORDER BY c.Name ASC, tas.Created DESC
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM Messaging.TicketAttachmentSettings tas
            LEFT JOIN Common.Companies c ON c.Id = tas.CompanyId
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<TicketAttachmentSettingsDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<TicketAttachmentSettingsDto>>
        {
            IsSuccess = true,
            Message = "System-wide ticket attachment settings retrieved successfully.",
            Data = new PagedResult<TicketAttachmentSettingsDto>
            {
                Items = items,
                PageNumber = sanitizedPageNumber,
                PageSize = sanitizedPageSize,
                TotalCount = totalCount,
                TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)sanitizedPageSize)
            }
        };
    }
}