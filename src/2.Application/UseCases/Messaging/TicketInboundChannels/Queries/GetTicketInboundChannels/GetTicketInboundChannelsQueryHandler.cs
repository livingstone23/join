using System;
using System.Linq;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketInboundChannels.Queries.GetTicketInboundChannels;

/// <summary>
/// Lista los <c>TicketInboundChannel</c> del tenant actual con Dapper, filtrando
/// por <c>CompanyId</c> y proyectando el token enmascarado (solo create lo expone).
/// </summary>
public sealed class GetTicketInboundChannelsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTicketInboundChannelsQuery, Response<IReadOnlyCollection<TicketInboundChannelDto>>>
{
    /// <inheritdoc />
    public async Task<Response<IReadOnlyCollection<TicketInboundChannelDto>>> Handle(
        GetTicketInboundChannelsQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyCollection<TicketInboundChannelDto>>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var activeOnly = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted)
            ? string.Empty
            : "AND tic.gcrecord = 0";

        var sql = $"""
            SELECT
                tic.id AS Id,
                tic.companyid AS CompanyId,
                tic.channelid AS ChannelId,
                cc.code AS ChannelCode,
                cc.name AS ChannelName,
                tic.description AS Description,
                tic.webhooktoken AS WebhookToken,
                tic.created AS CreatedAt
            FROM messaging.ticketinboundchannels tic
            INNER JOIN common.communicationchannels cc ON cc.id = tic.channelid
            WHERE tic.companyid = @TenantId
              {activeOnly}
            ORDER BY tic.created DESC;
            """;

        using var connection = connectionFactory.CreateConnection();

        var rows = (await connection.QueryAsync<TicketInboundChannelRow>(
            new CommandDefinition(sql, new { TenantId = TenantResolver.Resolve(currentUserService, request.CompanyId) }, cancellationToken: cancellationToken))).AsList();

        var items = rows.Select(MapToDto).ToList();

        return new Response<IReadOnlyCollection<TicketInboundChannelDto>>
        {
            IsSuccess = true,
            Message = "Ticket inbound channels retrieved successfully.",
            Data = items
        };
    }

    private static TicketInboundChannelDto MapToDto(TicketInboundChannelRow row) => new()
    {
        Id = row.Id,
        CompanyId = row.CompanyId,
        ChannelId = row.ChannelId,
        ChannelCode = row.ChannelCode ?? string.Empty,
        ChannelName = row.ChannelName,
        Description = row.Description,
        WebhookTokenMasked = MaskToken(row.WebhookToken),
        // El controller es responsable de construir el URL — depende de HttpContext.
        WebhookUrl = string.Empty,
        CreatedAt = row.CreatedAt
    };

    /// <summary>
    /// Enmascara el token conservando los últimos 4 caracteres: <c>"••••••••abcd"</c>.
    /// Si el token es demasiado corto para que valga la pena enmascararlo, devuelve
    /// solo asteriscos.
    /// </summary>
    internal static string MaskToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return string.Empty;
        }

        if (token.Length <= 4)
        {
            return new string('•', Math.Max(0, token.Length));
        }

        var last4 = token[^4..];
        return $"••••••••{last4}";
    }

    private sealed class TicketInboundChannelRow
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public Guid ChannelId { get; set; }
        public string? ChannelCode { get; set; }
        public string ChannelName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string WebhookToken { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
