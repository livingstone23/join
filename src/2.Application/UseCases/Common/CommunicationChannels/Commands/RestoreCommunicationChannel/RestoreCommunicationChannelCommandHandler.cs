using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.CommunicationChannels.Commands;

/// <summary>
/// Handles <see cref="RestoreCommunicationChannelCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the communication channel's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestoreCommunicationChannelCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreCommunicationChannelCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreCommunicationChannelCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<CommunicationChannel>(
            request.Id,
            request.CompanyId,
            cancellationToken: cancellationToken);
    }
}
