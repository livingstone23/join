using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.UpdateCalendarCompany;

/// <summary>
/// Handles <see cref="UpdateCalendarCompanyCommand"/>. Narrowing the business hours fails with
/// <c>CALENDAR_USER_HOURS_OUT_OF_COMPANY_RANGE</c> and the affected users; changing the time zone while future
/// activities exist returns a warning (stored times are UTC, their local time moves).
/// </summary>
public sealed class UpdateCalendarCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarCompanySettingsValidator settingsValidator,
    CalendarUserProvisioner provisioner)
    : IRequestHandler<UpdateCalendarCompanyCommand, Response<CalendarCompanyDto>>
{
    public async Task<Response<CalendarCompanyDto>> Handle(UpdateCalendarCompanyCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarCompanyDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarCompany>();
        var current = (await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0)).FirstOrDefault();
        var entity = current is null ? null : await repository.GetAsync(current.Id);
        if (entity is null)
        {
            return Response<CalendarCompanyDto>.Error(CalendarErrorCodes.CompanyNotConfigured, ["The company has no calendar configuration. Create it first."]);
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

        var rescheduleStatus = await settingsValidator.ResolveRescheduleStatusAsync(
            companyId, request.RescheduledByOthersStatusCompanyId ?? entity.RescheduledByOthersStatusCompanyId);
        if (rescheduleStatus is null)
        {
            return Response<CalendarCompanyDto>.Error(CalendarErrorCodes.StatusNotEnabled, ["The reschedule status must be an active status of the company."]);
        }

        var outOfRange = await settingsValidator.GetUsersOutOfRangeAsync(
            companyId, request.IsOpen24Hours, request.BusinessHoursStart, request.BusinessHoursEnd);
        if (outOfRange.Count > 0)
        {
            return Response<CalendarCompanyDto>.Error(CalendarErrorCodes.UserHoursOutOfCompanyRange, outOfRange);
        }

        var warnings = new List<string>();
        var newZone = request.TimeZone.Trim();
        if (!string.Equals(newZone, entity.TimeZone, StringComparison.Ordinal)
            && await unitOfWork.CalendarEvents.AnyAsync(e => e.CompanyId == companyId && e.GcRecord == 0
                && (e.EndUtc > DateTime.UtcNow || (e.RecurrenceRule != null && (e.RecurrenceEndUtc == null || e.RecurrenceEndUtc > DateTime.UtcNow))), cancellationToken))
        {
            warnings.Add("The time zone changed while future activities exist: they keep their UTC time, so their local time moves.");
        }

        entity.DefaultActivityUserId = request.DefaultActivityUserId;
        entity.TimeZone = newZone;
        entity.IsOpen24Hours = request.IsOpen24Hours;
        entity.BusinessHoursStart = request.IsOpen24Hours ? null : request.BusinessHoursStart;
        entity.BusinessHoursEnd = request.IsOpen24Hours ? null : request.BusinessHoursEnd;
        entity.DefaultCalendarName = CalendarCatalogRules.Clean(request.DefaultCalendarName) ?? entity.DefaultCalendarName;
        entity.RescheduledByOthersStatusCompanyId = rescheduleStatus.Id;

        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CalendarCompanyDto>
        {
            IsSuccess = true,
            Message = "Calendar configuration updated successfully.",
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
                RescheduledByOthersStatusName = rescheduleStatus.Name,
                Warnings = warnings
            }
        };
    }
}
