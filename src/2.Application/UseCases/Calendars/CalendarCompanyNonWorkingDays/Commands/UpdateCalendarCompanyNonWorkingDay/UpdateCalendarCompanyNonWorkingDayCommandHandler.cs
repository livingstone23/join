using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.UpdateCalendarCompanyNonWorkingDay;

/// <summary>
/// Handles <see cref="UpdateCalendarCompanyNonWorkingDayCommand"/>.
/// </summary>
public sealed class UpdateCalendarCompanyNonWorkingDayCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ICalendarMapper mapper)
    : IRequestHandler<UpdateCalendarCompanyNonWorkingDayCommand, Response<CalendarCompanyNonWorkingDayDto>>
{
    public async Task<Response<CalendarCompanyNonWorkingDayDto>> Handle(UpdateCalendarCompanyNonWorkingDayCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarCompanyNonWorkingDayDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarCompanyNonWorkingDay>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<CalendarCompanyNonWorkingDayDto>.Error(CalendarErrorCodes.NonWorkingDayNotFound, ["The holiday was not found."]);
        }

        if ((await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.Date == request.Date && x.Id != entity.Id && x.GcRecord == 0)).Any())
        {
            return Response<CalendarCompanyNonWorkingDayDto>.Error(CalendarErrorCodes.NonWorkingDayDuplicate, ["The company already has a holiday on this date."]);
        }

        entity.Date = request.Date;
        entity.Name = request.Name.Trim();
        entity.IsRecurringYearly = request.IsRecurringYearly;
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<CalendarCompanyNonWorkingDayDto> { IsSuccess = true, Data = mapper.ToDto(entity), Message = "Holiday updated successfully." };
    }
}
