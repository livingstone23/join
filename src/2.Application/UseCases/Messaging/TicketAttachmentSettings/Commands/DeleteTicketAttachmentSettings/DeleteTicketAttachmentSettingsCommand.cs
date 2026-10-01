using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;

/// <summary>
/// Command para el borrado lógico de la configuración de adjuntos de una
/// empresa.
/// </summary>
/// <param name="Id">Identificador de la configuración.</param>
public record DeleteTicketAttachmentSettingsCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;