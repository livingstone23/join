using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Queries.ResolveTicketInboundChannel;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.RegisterInboundTicket;
using JOIN.Services.WebApi.Services;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Messaging;

/// <summary>
/// Common flow of the anonymous inbound webhooks (SPEC 47 D): resolve the webhook token, expose its tenant to the
/// request and answer the provider with <see cref="InboundTicketResponse"/>. Every "token not usable" case is a bare
/// <c>404</c> — no body — so nothing confirms or denies whether a token exists.
/// </summary>
public abstract class InboundTicketWebhookController(ISender sender, ILogger logger) : ControllerBase
{
    /// <summary> Mediator used by the derived webhooks. </summary>
    protected ISender Sender { get; } = sender;

    /// <summary>
    /// Resolves the active inbound channel of <paramref name="token"/> whose channel has
    /// <paramref name="expectedChannelCode"/>, and makes its company the tenant of the request.
    /// </summary>
    protected async Task<ResolvedTicketInboundChannelDto?> ResolveChannelAsync(string token, string expectedChannelCode, CancellationToken cancellationToken)
    {
        var resolved = await Sender.Send(new ResolveTicketInboundChannelQuery(token, expectedChannelCode), cancellationToken);
        if (!resolved.IsSuccess || resolved.Data is null)
        {
            return null;
        }

        HttpContext.Items[CurrentUserService.InboundCompanyIdItemKey] = resolved.Data.CompanyId;
        return resolved.Data;
    }

    /// <summary>
    /// <c>404</c> without body. <c>NotFound()</c> would be turned into a ProblemDetails by <c>[ApiController]</c>;
    /// the webhooks answer nothing that could help probing tokens.
    /// </summary>
    protected IActionResult BareNotFound()
    {
        HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        return new EmptyResult();
    }

    /// <summary> Payload without a required field: 400 with the module error code. </summary>
    protected IActionResult InvalidPayload(string detail)
    {
        logger.LogWarning("[InboundWebhook] Rejected payload: {Detail}", detail);
        return BadRequest(Response<object>.Error(TicketInboundErrorCodes.PayloadInvalid, [detail]));
    }

    /// <summary> Sends the command and answers the provider: 200 with the code, bare 404 or 400 with the error. </summary>
    protected async Task<IActionResult> RegisterAsync(RegisterInboundTicketCommand command, CancellationToken cancellationToken)
    {
        var response = await Sender.Send(command, cancellationToken);
        if (response.IsSuccess && response.Data is not null)
        {
            return Ok(new InboundTicketResponse(response.Data.Code));
        }

        if (response.Message == TicketInboundErrorCodes.InboundChannelNotFound)
        {
            return BareNotFound();
        }

        logger.LogWarning(
            "[InboundWebhook] Inbound message {ExternalMessageId} of company {CompanyId} rejected: {Code}.",
            command.ExternalMessageId, command.CompanyId, response.Message);
        return BadRequest(Response<object>.Error(response.Message, response.Errors));
    }
}
