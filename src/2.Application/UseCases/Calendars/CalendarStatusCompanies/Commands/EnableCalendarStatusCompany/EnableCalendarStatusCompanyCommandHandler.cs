using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.EnableCalendarStatusCompany;

/// <summary>
/// Handles <see cref="EnableCalendarStatusCompanyCommand"/>.
/// </summary>
public sealed class EnableCalendarStatusCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<EnableCalendarStatusCompanyCommand, Response<CalendarStatusCompanyDto>>
{
    public async Task<Response<CalendarStatusCompanyDto>> Handle(EnableCalendarStatusCompanyCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarStatusCompanyDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var global = await unitOfWork.GetRepository<CalendarStatus>().GetAsync(request.CalendarStatusId);
        if (global is null || global.GcRecord != 0)
        {
            return Response<CalendarStatusCompanyDto>.Error(CalendarErrorCodes.StatusNotFound, ["The global status was not found."]);
        }

        var repository = unitOfWork.GetRepository<CalendarStatusCompany>();
        if ((await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.CalendarStatusId == global.Id && x.GcRecord == 0)).Any())
        {
            return Response<CalendarStatusCompanyDto>.Error(CalendarErrorCodes.StatusAlreadyEnabled, ["The status is already enabled in the company."]);
        }

        var entity = new CalendarStatusCompany
        {
            CompanyId = companyId,
            CalendarStatusId = global.Id,
            Name = global.Name,
            Color = global.Color,
            IsActive = true,
            SortOrder = global.SortOrder
        };

        await repository.InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CalendarStatusCompanyDto>
        {
            IsSuccess = true,
            Message = "Calendar status enabled successfully.",
            Data = CalendarCompanyCatalogDtos.ToDto(entity, global)
        };
    }
}
