using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.CreateCalendarCompany;

/// <summary>
/// Handles <see cref="CreateCalendarCompanyCommand"/>. Does not run <see cref="CalendarCompanyGuard"/> (R1): it is
/// the operation that creates the configuration. Saves the configuration first so the provisioner finds it.
/// </summary>
public sealed class CreateCalendarCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarCompanySettingsValidator settingsValidator,
    CalendarCompanyProvisioning provisioning,
    CalendarUserProvisioner provisioner)
    : IRequestHandler<CreateCalendarCompanyCommand, Response<CalendarCompanyDto>>
{
    public async Task<Response<CalendarCompanyDto>> Handle(CreateCalendarCompanyCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarCompanyDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarCompany>();
        if ((await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0)).Any())
        {
            return Response<CalendarCompanyDto>.Error(CalendarErrorCodes.CompanyAlreadyConfigured, ["The company already has a calendar configuration."]);
        }

        var zoneAndHours = CalendarCompanySettingsValidator.ValidateZoneAndHours(
            request.TimeZone, request.IsOpen24Hours, request.BusinessHoursStart, request.BusinessHoursEnd);
        if (!zoneAndHours.IsSuccess)
        {
            return Response<CalendarCompanyDto>.Error(zoneAndHours.Message, zoneAndHours.Errors);
        }

        if (!await settingsValidator.IsCompanyMemberAsync(companyId, request.DefaultActivityUserId))
        {
            return Response<CalendarCompanyDto>.Error(CalendarErrorCodes.DefaultUserNotInCompany, ["The default activity user must be an active user of the company."]);
        }

        var rescheduleStatus = await settingsValidator.ResolveRescheduleStatusAsync(companyId, request.RescheduledByOthersStatusCompanyId);
        if (rescheduleStatus is null)
        {
            return Response<CalendarCompanyDto>.Error(CalendarErrorCodes.StatusNotEnabled,
                ["The reschedule status must be an active status of the company (default: RESCHEDULED). Sync the catalogs first."]);
        }

        var entity = new CalendarCompany
        {
            CompanyId = companyId,
            DefaultActivityUserId = request.DefaultActivityUserId,
            TimeZone = request.TimeZone.Trim(),
            IsOpen24Hours = request.IsOpen24Hours,
            BusinessHoursStart = request.IsOpen24Hours ? null : request.BusinessHoursStart,
            BusinessHoursEnd = request.IsOpen24Hours ? null : request.BusinessHoursEnd,
            DefaultCalendarName = CalendarCatalogRules.Clean(request.DefaultCalendarName) ?? "Mi calendario",
            RescheduledByOthersStatusCompanyId = rescheduleStatus.Id
        };

        await repository.InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var provisioned = await provisioning.ProvisionPendingUsersAsync(companyId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CalendarCompanyDto>
        {
            IsSuccess = true,
            Message = $"Calendar configuration created successfully. Users provisioned: {provisioned.Count}.",
            Data = new CalendarCompanyDto
            {
                IsConfigured = true,
                ModuleEnabled = await provisioner.IsModuleActiveAsync(companyId),
                Id = entity.Id,
                DefaultActivityUserId = entity.DefaultActivityUserId,
                TimeZone = entity.TimeZone,
                IsOpen24Hours = entity.IsOpen24Hours,
                BusinessHoursStart = entity.BusinessHoursStart,
                BusinessHoursEnd = entity.BusinessHoursEnd,
                DefaultCalendarName = entity.DefaultCalendarName,
                RescheduledByOthersStatusCompanyId = entity.RescheduledByOthersStatusCompanyId,
                RescheduledByOthersStatusName = rescheduleStatus.Name
            }
        };
    }
}
