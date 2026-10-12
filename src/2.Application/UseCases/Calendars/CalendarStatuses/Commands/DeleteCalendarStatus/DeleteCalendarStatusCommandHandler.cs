using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.DeleteCalendarStatus;

/// <summary>
/// Handles <see cref="DeleteCalendarStatusCommand"/>: the five seeded statuses are required by the logic
/// (<c>CALENDAR_STATUS_REQUIRED_BY_SYSTEM</c>); a status enabled in any company answers <c>CALENDAR_STATUS_IN_USE</c>.
/// </summary>
public sealed class DeleteCalendarStatusCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteCalendarStatusCommand, Response<bool>>
{
    /// <summary> Status codes that cannot be deleted or deactivated in the global catalog. </summary>
    public static readonly string[] SystemCodes =
    [
        CalendarCodes.Statuses.Pending,
        CalendarCodes.Statuses.Confirmed,
        CalendarCodes.Statuses.Rescheduled,
        CalendarCodes.Statuses.Cancelled,
        CalendarCodes.Statuses.Completed
    ];

    public async Task<Response<bool>> Handle(DeleteCalendarStatusCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<bool>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarStatus>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.GcRecord != 0)
        {
            return Response<bool>.Error(CalendarErrorCodes.StatusNotFound, ["The status was not found."]);
        }

        if (SystemCodes.Contains(entity.Code))
        {
            return Response<bool>.Error(CalendarErrorCodes.StatusRequiredBySystem, [$"{entity.Code} is required by the system."]);
        }

        var enabledSomewhere = (await unitOfWork.GetRepository<CalendarStatusCompany>()
                .GetAllIncludingDeletedAsync(x => x.CalendarStatusId == entity.Id && x.GcRecord == 0))
            .Any();
        if (enabledSomewhere)
        {
            return Response<bool>.Error(CalendarErrorCodes.StatusInUse, ["The status is enabled in at least one company."]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<bool> { IsSuccess = true, Data = true, Message = "Calendar status deleted successfully." };
    }
}
