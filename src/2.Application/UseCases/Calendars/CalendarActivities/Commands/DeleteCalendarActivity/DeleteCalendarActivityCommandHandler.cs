using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.DeleteCalendarActivity;

/// <summary>
/// Handles <see cref="DeleteCalendarActivityCommand"/>: <c>PERSON_REVIEW</c> is used by the channel agent (SPEC 45)
/// and cannot be deleted; a type enabled in any company answers <c>CALENDAR_ACTIVITY_IN_USE</c>.
/// </summary>
public sealed class DeleteCalendarActivityCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteCalendarActivityCommand, Response<bool>>
{
    /// <summary> Activity codes the logic depends on. </summary>
    public static readonly string[] SystemCodes = [CalendarCodes.Activities.PersonReview];

    public async Task<Response<bool>> Handle(DeleteCalendarActivityCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<bool>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarActivity>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.GcRecord != 0)
        {
            return Response<bool>.Error(CalendarErrorCodes.ActivityNotFound, ["The activity type was not found."]);
        }

        if (SystemCodes.Contains(entity.Code))
        {
            return Response<bool>.Error(CalendarErrorCodes.ActivityRequiredBySystem, [$"{entity.Code} is required by the system."]);
        }

        var enabledSomewhere = (await unitOfWork.GetRepository<CalendarActivityCompany>()
                .GetAllIncludingDeletedAsync(x => x.CalendarActivityId == entity.Id && x.GcRecord == 0))
            .Any();
        if (enabledSomewhere)
        {
            return Response<bool>.Error(CalendarErrorCodes.ActivityInUse, ["The activity type is enabled in at least one company."]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<bool> { IsSuccess = true, Data = true, Message = "Calendar activity type deleted successfully." };
    }
}
