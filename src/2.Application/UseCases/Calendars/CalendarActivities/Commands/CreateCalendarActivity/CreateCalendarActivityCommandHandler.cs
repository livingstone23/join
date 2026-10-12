using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.CreateCalendarActivity;

/// <summary>
/// Handles <see cref="CreateCalendarActivityCommand"/>: the code is unique among active types.
/// </summary>
public sealed class CreateCalendarActivityCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ICalendarMapper mapper)
    : IRequestHandler<CreateCalendarActivityCommand, Response<CalendarActivityDto>>
{
    public async Task<Response<CalendarActivityDto>> Handle(CreateCalendarActivityCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CalendarActivityDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarActivity>();
        var code = CalendarCatalogRules.NormalizeCode(request.Code);
        if ((await repository.GetAllIncludingDeletedAsync(x => x.Code == code && x.GcRecord == 0)).Any())
        {
            return Response<CalendarActivityDto>.Error(CalendarErrorCodes.ActivityCodeDuplicate, ["Another active activity type already uses this code."]);
        }

        var entity = new CalendarActivity
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = CalendarCatalogRules.Clean(request.Description),
            Color = request.Color.Trim(),
            Icon = CalendarCatalogRules.Clean(request.Icon),
            DefaultDurationMinutes = request.DefaultDurationMinutes,
            AllowsOverlap = request.AllowsOverlap,
            RequiresBusinessHours = request.RequiresBusinessHours,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder
        };

        await repository.InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CalendarActivityDto>
        {
            IsSuccess = true,
            Message = "Calendar activity type created successfully.",
            Data = mapper.ToDto(entity)
        };
    }
}
