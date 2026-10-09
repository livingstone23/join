using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;

/// <summary>
/// Restores a logically deleted ticket attachment settings configuration (SPEC 41, Etapa 4). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the ticket attachment settings configuration to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreTicketAttachmentSettingsCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
