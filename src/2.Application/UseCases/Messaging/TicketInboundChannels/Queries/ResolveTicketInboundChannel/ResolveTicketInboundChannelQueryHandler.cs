using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketInboundChannels.Queries.ResolveTicketInboundChannel;

/// <summary>
/// Handles <see cref="ResolveTicketInboundChannelQuery"/>. It is the only tenant-less query of the module: the
/// request is anonymous and the token itself is the credential that identifies the tenant, so the filter is the
/// token (globally unique among active rows) plus <c>GcRecord = 0</c> on both tables. Every "no match" (unknown,
/// soft-deleted, inactive channel or another channel code) is the same <see cref="TicketInboundErrorCodes.InboundChannelNotFound"/>.
/// </summary>
public sealed class ResolveTicketInboundChannelQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<ResolveTicketInboundChannelQuery, Response<ResolvedTicketInboundChannelDto>>
{
    private const string Sql = """
        SELECT tic.id AS Id,
               tic.companyid AS CompanyId,
               tic.channelid AS ChannelId,
               cc.code AS ChannelCode
          FROM messaging.ticketinboundchannels tic
          JOIN common.communicationchannels cc ON cc.id = tic.channelid
         WHERE tic.webhooktoken = @WebhookToken
           AND tic.gcrecord = 0
           AND cc.gcrecord = 0
           AND cc.isactive = TRUE
           AND UPPER(cc.code) = UPPER(@ExpectedChannelCode)
         LIMIT 1;
        """;

    /// <inheritdoc />
    public async Task<Response<ResolvedTicketInboundChannelDto>> Handle(
        ResolveTicketInboundChannelQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.WebhookToken) || string.IsNullOrWhiteSpace(request.ExpectedChannelCode))
        {
            return NotFound();
        }

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ResolvedRow>(new CommandDefinition(
            Sql,
            new { WebhookToken = request.WebhookToken.Trim(), request.ExpectedChannelCode },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return NotFound();
        }

        return new Response<ResolvedTicketInboundChannelDto>
        {
            IsSuccess = true,
            Data = new ResolvedTicketInboundChannelDto(row.Id, row.CompanyId, row.ChannelId, row.ChannelCode ?? string.Empty)
        };
    }

    private static Response<ResolvedTicketInboundChannelDto> NotFound() =>
        Response<ResolvedTicketInboundChannelDto>.Error(
            TicketInboundErrorCodes.InboundChannelNotFound,
            ["No active inbound channel matches the webhook token."]);

    private sealed class ResolvedRow
    {
        public Guid Id { get; init; }
        public Guid CompanyId { get; init; }
        public Guid ChannelId { get; init; }
        public string? ChannelCode { get; init; }
    }
}
