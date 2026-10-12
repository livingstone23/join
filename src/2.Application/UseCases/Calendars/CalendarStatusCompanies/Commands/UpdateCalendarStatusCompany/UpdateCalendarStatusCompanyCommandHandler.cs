using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.UpdateCalendarStatusCompany;

/// <summary>
/// Handles <see cref="UpdateCalendarStatusCompanyCommand"/>; deactivating runs <see cref="CalendarStatusCompanyRemovalGuard"/>.
/// </summary>
public sealed class UpdateCalendarStatusCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarStatusCompanyRemovalGuard removalGuard)
    : IRequestHandler<UpdateCalendarStatusCompanyCommand, Response<CalendarStatusCompanyDto>>
{
    public async Task<Response<CalendarStatusCompanyDto>> Handle(UpdateCalendarStatusCompanyCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarStatusCompanyDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarStatusCompany>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<CalendarStatusCompanyDto>.Error(CalendarErrorCodes.StatusNotFound, ["The status was not found in the company."]);
        }

        var global = await unitOfWork.GetRepository<CalendarStatus>().GetAsync(entity.CalendarStatusId);
        if (entity.IsActive && !request.IsActive)
        {
            var removable = await removalGuard.CanRemoveAsync(entity, global, cancellationToken);
            if (!removable.IsSuccess)
            {
                return Response<CalendarStatusCompanyDto>.Error(removable.Message, removable.Errors);
            }
        }

        entity.Name = request.Name.Trim();
        entity.Color = request.Color.Trim();
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;

        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CalendarStatusCompanyDto>
        {
            IsSuccess = true,
            Message = "Calendar status updated successfully.",
            Data = CalendarCompanyCatalogDtos.ToDto(entity, global ?? new CalendarStatus())
        };
    }
}
