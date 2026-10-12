using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.UpdateCalendarActivity;

/// <summary>
/// Handles <see cref="UpdateCalendarActivityCommand"/>.
/// </summary>
public sealed class UpdateCalendarActivityCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ICalendarMapper mapper)
    : IRequestHandler<UpdateCalendarActivityCommand, Response<CalendarActivityDto>>
{
    public async Task<Response<CalendarActivityDto>> Handle(UpdateCalendarActivityCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CalendarActivityDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarActivity>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.GcRecord != 0)
        {
            return Response<CalendarActivityDto>.Error(CalendarErrorCodes.ActivityNotFound, ["The activity type was not found."]);
        }

        entity.Name = request.Name.Trim();
        entity.Description = CalendarCatalogRules.Clean(request.Description);
        entity.Color = request.Color.Trim();
        entity.Icon = CalendarCatalogRules.Clean(request.Icon);
        entity.DefaultDurationMinutes = request.DefaultDurationMinutes;
        entity.AllowsOverlap = request.AllowsOverlap;
        entity.RequiresBusinessHours = request.RequiresBusinessHours;
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;

        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CalendarActivityDto>
        {
            IsSuccess = true,
            Message = "Calendar activity type updated successfully.",
            Data = mapper.ToDto(entity)
        };
    }
}
