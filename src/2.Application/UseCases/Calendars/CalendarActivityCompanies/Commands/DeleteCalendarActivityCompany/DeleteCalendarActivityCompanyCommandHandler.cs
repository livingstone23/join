using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.DeleteCalendarActivityCompany;

/// <summary>
/// Handles <see cref="DeleteCalendarActivityCompanyCommand"/> (SPEC 44: "Bloqueado si hay actividades de ese tipo
/// (<c>CALENDAR_ACTIVITY_IN_USE</c>); en ese caso se desactiva"). A type in use is deactivated and the response
/// carries <c>CALENDAR_ACTIVITY_IN_USE</c> in <c>Errors</c> with <c>Data = false</c>.
/// </summary>
public sealed class DeleteCalendarActivityCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteCalendarActivityCompanyCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(DeleteCalendarActivityCompanyCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<bool>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarActivityCompany>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<bool>.Error(CalendarErrorCodes.ActivityNotFound, ["The activity type was not found in the company."]);
        }

        var inUse = await unitOfWork.CalendarEvents.AnyAsync(e => e.CompanyId == companyId && e.CalendarActivityCompanyId == entity.Id && e.GcRecord == 0, cancellationToken);
        if (inUse)
        {
            entity.IsActive = false;
            await repository.UpdateAsync(entity);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new Response<bool>
            {
                IsSuccess = true,
                Data = false,
                Message = "The activity type has activities; it was deactivated instead of deleted.",
                Errors = [CalendarErrorCodes.ActivityInUse]
            };
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<bool> { IsSuccess = true, Data = true, Message = "Calendar activity type removed from the company." };
    }
}
