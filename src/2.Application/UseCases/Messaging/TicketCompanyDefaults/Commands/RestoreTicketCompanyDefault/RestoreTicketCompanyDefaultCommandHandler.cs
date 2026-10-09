using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;

/// <summary>
/// Handles <see cref="RestoreTicketCompanyDefaultCommand"/> (SPEC 41, Etapa 4): guards the tenant and the <c>SuperAdmin</c> role,
/// then delegates to <see cref="SoftDeleteRestorer"/>. The configuration is not restored while a catalog it points to (status, complexity, time unit, area, project, channel) is deleted, nor when the company already has an active configuration (filtered unique index on <c>CompanyId</c>).
/// </summary>
public sealed class RestoreTicketCompanyDefaultCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreTicketCompanyDefaultCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreTicketCompanyDefaultCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<TicketCompanyDefault>(
            request.Id,
            request.CompanyId,
            isParentDeleted: async (entity, _) =>
                (entity.TicketStatusDefaultId is { } statusId && await restorer.IsParentDeletedAsync<TicketStatus>(statusId))
                || (entity.TicketComplexityDefaultId is { } complexityId && await restorer.IsParentDeletedAsync<TicketComplexity>(complexityId))
                || (entity.TimeUnitDefaultId is { } timeUnitId && await restorer.IsParentDeletedAsync<TimeUnit>(timeUnitId))
                || (entity.AreaDefaultId is { } areaId && await restorer.IsParentDeletedAsync<Area>(areaId))
                || (entity.ProjectDefaultId is { } projectId && await restorer.IsParentDeletedAsync<Project>(projectId))
                || (entity.ChannelDefaultId is { } channelId && await restorer.IsParentDeletedAsync<CommunicationChannel>(channelId)),
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<TicketCompanyDefault>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId),
            cancellationToken: cancellationToken);
    }
}
