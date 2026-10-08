using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using JOIN.Domain.Support;
using MediatR;
using TicketAttachmentSettingsEntity = JOIN.Domain.Messaging.TicketAttachmentSettings;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.RestoreTicketDocument;

/// <summary>
/// Handles <see cref="RestoreTicketDocumentCommand"/> (SPEC 41, Etapa 4): guards the tenant and the <c>SuperAdmin</c>
/// role, then delegates to <see cref="SoftDeleteRestorer"/>. The attachment is not restored while its ticket is deleted
/// (<c>PARENT_DELETED</c>), nor when the ticket already holds <c>MaxFilesPerTicket</c> active attachments
/// (<c>MAX_FILES_PER_TICKET_REACHED</c>). The daily upload quota does not apply. The stored file is never removed by
/// the soft delete, so the storage is not touched.
/// </summary>
public sealed class RestoreTicketDocumentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreTicketDocumentCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreTicketDocumentCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<TicketDocument>(
            request.Id,
            request.CompanyId,
            isParentDeleted: (document, _) => restorer.IsParentDeletedAsync<Ticket>(document.TicketId),
            // Runs after the common checks and before the restore; an error aborts it (nothing is saved).
            restoreCascade: (document, _, _) => CheckTicketAndQuotaAsync(document, request.TicketId),
            cancellationToken: cancellationToken);
    }

    private async Task<Response<Guid>?> CheckTicketAndQuotaAsync(TicketDocument document, Guid routeTicketId)
    {
        if (document.TicketId != routeTicketId)
        {
            return Response<Guid>.Error("NOT_FOUND", [$"TicketDocument '{document.Id}' was not found."]);
        }

        var settings = (await unitOfWork.GetRepository<TicketAttachmentSettingsEntity>()
            .GetAllIncludingDeletedAsync(s => s.GcRecord == 0 && s.CompanyId == document.CompanyId)).FirstOrDefault();
        if (settings is null)
        {
            // Without settings there is no per-ticket limit to enforce.
            return null;
        }

        var activeDocuments = (await unitOfWork.GetRepository<TicketDocument>()
            .GetAllIncludingDeletedAsync(d => d.GcRecord == 0 && d.TicketId == document.TicketId)).Count();
        if (activeDocuments >= settings.MaxFilesPerTicket)
        {
            return Response<Guid>.Error(
                "MAX_FILES_PER_TICKET_REACHED",
                [$"The ticket already has {activeDocuments} attachments (max {settings.MaxFilesPerTicket})."]);
        }

        return null;
    }
}
