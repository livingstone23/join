namespace JOIN.Application.UseCases.Messaging.Tickets;

/// <summary>
/// Shape agnóstico de transporte para "un archivo que entra al sistema".
/// El Application layer opera contra este record, no contra tipos de
/// <c>Microsoft.AspNetCore.Http</c> (<c>IFormFile</c>) — el controller
/// hace la adaptación (vía <c>file.OpenReadStream()</c> y compañía). Esta
/// declaración vive acá porque SPEC 36 la introduce y SPEC 37 (inbound
/// WhatsApp/correo) la consume tal cual.
/// </summary>
/// <param name="Content">Stream del archivo. La implementación es dueña
/// de su ciclo de vida y debe copiar/disponer según corresponda.</param>
/// <param name="FileName">Nombre original del archivo (ej. "report.pdf") —
/// se persiste en <c>TicketDocument.OriginalName</c> y se usa para la
/// descarga.</param>
/// <param name="ContentType">MIME type (ej. "application/pdf").</param>
/// <param name="Length">Tamaño en bytes declarado por el transporte —
/// se persiste en <c>TicketDocument.SizeBytes</c> y se usa para validar
/// <c>MaxFileSizeBytes</c> sin volver a golpear el storage.</param>
public sealed record InboundAttachment(
    Stream Content,
    string FileName,
    string ContentType,
    long Length);