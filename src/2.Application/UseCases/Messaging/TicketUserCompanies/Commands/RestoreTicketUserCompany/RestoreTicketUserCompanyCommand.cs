using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.RestoreTicketUserCompany;

/// <summary>
/// Restores a logically deleted ticket roster entry (SPEC 41, Etapa 4). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the ticket roster entry to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreTicketUserCompanyCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
