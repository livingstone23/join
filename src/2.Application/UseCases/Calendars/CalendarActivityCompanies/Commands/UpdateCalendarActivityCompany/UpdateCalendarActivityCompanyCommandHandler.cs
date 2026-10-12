using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.UpdateCalendarActivityCompany;

/// <summary>
/// Handles <see cref="UpdateCalendarActivityCompanyCommand"/>.
/// </summary>
public sealed class UpdateCalendarActivityCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarCatalogReader catalogReader)
    : IRequestHandler<UpdateCalendarActivityCompanyCommand, Response<CalendarActivityCompanyDto>>
{
    public async Task<Response<CalendarActivityCompanyDto>> Handle(UpdateCalendarActivityCompanyCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarActivityCompanyDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarActivityCompany>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<CalendarActivityCompanyDto>.Error(CalendarErrorCodes.ActivityNotFound, ["The activity type was not found in the company."]);
        }

        var initialStatus = await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(request.InitialCalendarStatusCompanyId);
        if (await catalogReader.GetUsableStatusAsync(companyId, initialStatus) is null)
        {
            return Response<CalendarActivityCompanyDto>.Error(CalendarErrorCodes.StatusNotEnabled, ["The initial status must be an active status of the company."]);
        }

        entity.Name = request.Name.Trim();
        entity.Color = request.Color.Trim();
        entity.DefaultDurationMinutes = request.DefaultDurationMinutes;
        entity.AllowsOverlap = request.AllowsOverlap;
        entity.RequiresBusinessHours = request.RequiresBusinessHours;
        entity.InitialCalendarStatusCompanyId = initialStatus!.Id;
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;

        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var global = await unitOfWork.GetRepository<CalendarActivity>().GetAsync(entity.CalendarActivityId);
        return new Response<CalendarActivityCompanyDto>
        {
            IsSuccess = true,
            Message = "Calendar activity type updated successfully.",
            Data = CalendarCompanyCatalogDtos.ToDto(entity, global ?? new CalendarActivity(), initialStatus.Name)
        };
    }
}
