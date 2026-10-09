using JOIN.Application.Common;
using JOIN.Application.Interface;
using MediatR;
using TicketAttachmentSettingsEntity = JOIN.Domain.Messaging.TicketAttachmentSettings;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;

/// <summary>
/// Handles <see cref="RestoreTicketAttachmentSettingsCommand"/> (SPEC 41, Etapa 4): guards the tenant and the <c>SuperAdmin</c> role,
/// then delegates to <see cref="SoftDeleteRestorer"/>. The settings are not restored when the company already has an active configuration (<c>UX_TicketAttachmentSettings_Company_Active</c>).
/// </summary>
public sealed class RestoreTicketAttachmentSettingsCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreTicketAttachmentSettingsCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreTicketAttachmentSettingsCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<TicketAttachmentSettingsEntity>(
            request.Id,
            request.CompanyId,
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<TicketAttachmentSettingsEntity>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId),
            cancellationToken: cancellationToken);
    }
}
