using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.CreateCalendarCompanyNonWorkingDay;

/// <summary>
/// Handles <see cref="CreateCalendarCompanyNonWorkingDayCommand"/>: one active holiday per date.
/// </summary>
public sealed class CreateCalendarCompanyNonWorkingDayCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ICalendarMapper mapper)
    : IRequestHandler<CreateCalendarCompanyNonWorkingDayCommand, Response<CalendarCompanyNonWorkingDayDto>>
{
    public async Task<Response<CalendarCompanyNonWorkingDayDto>> Handle(CreateCalendarCompanyNonWorkingDayCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarCompanyNonWorkingDayDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarCompanyNonWorkingDay>();
        if ((await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.Date == request.Date && x.GcRecord == 0)).Any())
        {
            return Response<CalendarCompanyNonWorkingDayDto>.Error(CalendarErrorCodes.NonWorkingDayDuplicate, ["The company already has a holiday on this date."]);
        }

        var entity = new CalendarCompanyNonWorkingDay
        {
            CompanyId = companyId,
            Date = request.Date,
            Name = request.Name.Trim(),
            IsRecurringYearly = request.IsRecurringYearly
        };

        await repository.InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<CalendarCompanyNonWorkingDayDto> { IsSuccess = true, Data = mapper.ToDto(entity), Message = "Holiday created successfully." };
    }
}
