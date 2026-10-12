using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.DeleteCalendarStatusCompany;

/// <summary>
/// Handles <see cref="DeleteCalendarStatusCompanyCommand"/>.
/// </summary>
public sealed class DeleteCalendarStatusCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarStatusCompanyRemovalGuard removalGuard)
    : IRequestHandler<DeleteCalendarStatusCompanyCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(DeleteCalendarStatusCompanyCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<bool>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarStatusCompany>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<bool>.Error(CalendarErrorCodes.StatusNotFound, ["The status was not found in the company."]);
        }

        var global = await unitOfWork.GetRepository<CalendarStatus>().GetAsync(entity.CalendarStatusId);
        var removable = await removalGuard.CanRemoveAsync(entity, global, cancellationToken);
        if (!removable.IsSuccess)
        {
            return removable;
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<bool> { IsSuccess = true, Data = true, Message = "Calendar status removed from the company." };
    }
}
