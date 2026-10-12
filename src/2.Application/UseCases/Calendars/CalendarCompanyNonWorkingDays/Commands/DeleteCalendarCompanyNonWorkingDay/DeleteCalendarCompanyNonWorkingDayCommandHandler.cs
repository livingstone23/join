using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.DeleteCalendarCompanyNonWorkingDay;

/// <summary>
/// Handles <see cref="DeleteCalendarCompanyNonWorkingDayCommand"/>.
/// </summary>
public sealed class DeleteCalendarCompanyNonWorkingDayCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<DeleteCalendarCompanyNonWorkingDayCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(DeleteCalendarCompanyNonWorkingDayCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<bool>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarCompanyNonWorkingDay>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<bool>.Error(CalendarErrorCodes.NonWorkingDayNotFound, ["The holiday was not found."]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<bool> { IsSuccess = true, Data = true, Message = "Holiday deleted successfully." };
    }
}
