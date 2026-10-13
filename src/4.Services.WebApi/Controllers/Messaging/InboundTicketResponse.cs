namespace JOIN.Services.WebApi.Controllers.Messaging;

/// <summary>
/// Body answered to the webhook provider (SPEC 47 D): only the ticket code. The provider needs a 2xx to stop
/// retrying; the internal detail of the ticket (users, ids) never leaves the network.
/// </summary>
/// <param name="TicketCode">Code of the created ticket, or of the existing one on a retry.</param>
public sealed record InboundTicketResponse(string TicketCode);
