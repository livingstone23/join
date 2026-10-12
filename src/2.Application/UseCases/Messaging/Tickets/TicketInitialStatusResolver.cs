using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;

namespace JOIN.Application.UseCases.Messaging.Tickets;

/// <summary>
/// Resolves the status a new ticket starts in (SPEC 42). A status sent by the client wins when it is
/// active, not final and belongs to the company; otherwise the company's
/// <see cref="TicketCompanyDefault.TicketStatusDefaultId"/> applies. SPEC 37 transition rules do not
/// apply here: a new ticket has no source status. Shared with channel ingestion (SPEC 47).
/// </summary>
public sealed class TicketInitialStatusResolver(IUnitOfWork unitOfWork)
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Returns the status identifier a new ticket must be created with, or an error response.
    /// </summary>
    /// <param name="companyId">Tenant identifier.</param>
    /// <param name="requestedStatusId">Status sent by the client, or <c>null</c> to use the company default.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task<Response<Guid>> ResolveAsync(Guid companyId, Guid? requestedStatusId, CancellationToken cancellationToken)
    {
        var statusRepository = _unitOfWork.GetRepository<TicketStatus>();

        if (requestedStatusId is { } requested)
        {
            var chosen = await statusRepository.GetAsync(requested);
            return IsUsableInitialStatus(chosen, companyId)
                ? Ok(chosen!.Id)
                : Response<Guid>.Error("INVALID_TICKET_STATUS",
                    ["The ticket status must be active, not final and belong to the current company."]);
        }

        var defaults = (await _unitOfWork.GetRepository<TicketCompanyDefault>().GetAllAsync())
            .FirstOrDefault(d => d.CompanyId == companyId && d.GcRecord == 0);

        if (defaults?.TicketStatusDefaultId is not { } defaultId)
        {
            return Response<Guid>.Error("TICKET_DEFAULT_STATUS_NOT_CONFIGURED",
                ["The company has no initial ticket status configured in TicketCompanyDefaults."]);
        }

        var configured = await statusRepository.GetAsync(defaultId);
        return IsUsableInitialStatus(configured, companyId)
            ? Ok(configured!.Id)
            : Response<Guid>.Error("TICKET_DEFAULT_STATUS_INVALID",
                ["The configured initial ticket status is missing, inactive, final or belongs to another company."]);
    }

    /// <summary>
    /// A status can start a ticket when it exists, is not soft-deleted, is active, belongs to the
    /// company and is not final (final states are reached only through FinishTicket, SPEC 37).
    /// </summary>
    public static bool IsUsableInitialStatus(TicketStatus? status, Guid companyId) =>
        status is not null && status.GcRecord == 0 && status.IsActive && status.CompanyId == companyId && !status.IsFinal;

    private static Response<Guid> Ok(Guid id) => new() { IsSuccess = true, Data = id };
}
