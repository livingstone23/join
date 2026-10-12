using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.DeleteCalendarStatus;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.UpdateCalendarStatus;

/// <summary>
/// Handles <see cref="UpdateCalendarStatusCommand"/>.
/// </summary>
public sealed class UpdateCalendarStatusCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ICalendarMapper mapper)
    : IRequestHandler<UpdateCalendarStatusCommand, Response<CalendarStatusDto>>
{
    public async Task<Response<CalendarStatusDto>> Handle(UpdateCalendarStatusCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CalendarStatusDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarStatus>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.GcRecord != 0)
        {
            return Response<CalendarStatusDto>.Error(CalendarErrorCodes.StatusNotFound, ["The status was not found."]);
        }

        if (!request.IsActive && DeleteCalendarStatusCommandHandler.SystemCodes.Contains(entity.Code))
        {
            return Response<CalendarStatusDto>.Error(CalendarErrorCodes.StatusRequiredBySystem, [$"{entity.Code} is required by the system and cannot be deactivated."]);
        }

        entity.Name = request.Name.Trim();
        entity.Description = CalendarCatalogRules.Clean(request.Description);
        entity.Color = request.Color.Trim();
        entity.BlocksTime = request.BlocksTime;
        entity.IsFinal = request.IsFinal;
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;

        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CalendarStatusDto> { IsSuccess = true, Message = "Calendar status updated successfully.", Data = mapper.ToDto(entity) };
    }
}
