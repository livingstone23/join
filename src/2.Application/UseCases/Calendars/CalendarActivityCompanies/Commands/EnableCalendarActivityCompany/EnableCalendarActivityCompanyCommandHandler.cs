using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.EnableCalendarActivityCompany;

/// <summary>
/// Handles <see cref="EnableCalendarActivityCompanyCommand"/>.
/// </summary>
public sealed class EnableCalendarActivityCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarCatalogReader catalogReader)
    : IRequestHandler<EnableCalendarActivityCompanyCommand, Response<CalendarActivityCompanyDto>>
{
    public async Task<Response<CalendarActivityCompanyDto>> Handle(EnableCalendarActivityCompanyCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarActivityCompanyDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var global = await unitOfWork.GetRepository<CalendarActivity>().GetAsync(request.CalendarActivityId);
        if (global is null || global.GcRecord != 0)
        {
            return Response<CalendarActivityCompanyDto>.Error(CalendarErrorCodes.ActivityNotFound, ["The global activity type was not found."]);
        }

        var repository = unitOfWork.GetRepository<CalendarActivityCompany>();
        if ((await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.CalendarActivityId == global.Id && x.GcRecord == 0)).Any())
        {
            return Response<CalendarActivityCompanyDto>.Error(CalendarErrorCodes.ActivityAlreadyEnabled, ["The activity type is already enabled in the company."]);
        }

        var initialStatus = request.InitialCalendarStatusCompanyId is { } statusId
            ? await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(statusId)
            : await catalogReader.GetCompanyStatusByCodeAsync(companyId, CalendarCodes.Statuses.Pending);
        if (await catalogReader.GetUsableStatusAsync(companyId, initialStatus) is null)
        {
            return Response<CalendarActivityCompanyDto>.Error(CalendarErrorCodes.StatusNotEnabled, ["The initial status must be an active status of the company."]);
        }

        var entity = new CalendarActivityCompany
        {
            CompanyId = companyId,
            CalendarActivityId = global.Id,
            Name = global.Name,
            Color = global.Color,
            DefaultDurationMinutes = global.DefaultDurationMinutes,
            AllowsOverlap = global.AllowsOverlap,
            RequiresBusinessHours = global.RequiresBusinessHours,
            InitialCalendarStatusCompanyId = initialStatus!.Id,
            IsActive = true,
            SortOrder = global.SortOrder
        };

        await repository.InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CalendarActivityCompanyDto>
        {
            IsSuccess = true,
            Message = "Calendar activity type enabled successfully.",
            Data = CalendarCompanyCatalogDtos.ToDto(entity, global, initialStatus.Name)
        };
    }
}
