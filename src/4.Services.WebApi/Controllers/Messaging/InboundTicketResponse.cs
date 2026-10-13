namespace JOIN.Services.WebApi.Controllers.Inbound;

/// <summary>
/// Cuerpo mínimo que devuelven los webhooks anónimos de ingesta de tickets
/// (SPEC 47 F5/F6). Los proveedores externos (Twilio, SendGrid) solo
/// necesitan un <c>2xx</c> con algo para no reintentar — exponer el
/// <c>Response&lt;TicketDto&gt;</c> completo filtraría identificadores
/// internos (ids de usuarios, GUIDs de catálogos) hacia afuera de la red.
/// </summary>
/// <param name="TicketId">Id del ticket creado (o del existente si el proveedor reintentó).</param>
/// <param name="TicketCode">Código humano del ticket (ej. "TICK-202610-0042").</param>
/// <param name="IsDuplicate">
/// <c>true</c> cuando el handler detectó que el <c>ExternalMessageId</c> ya
/// tenía un ticket registrado y devolvió el existente sin crear uno nuevo
/// (idempotencia de reintentos). <c>false</c> cuando el ticket se acaba de crear.
/// </param>
public sealed record InboundTicketResponse(
    Guid TicketId,
    string TicketCode,
    bool IsDuplicate);
