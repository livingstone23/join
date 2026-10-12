using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Security;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods;

/// <summary>
/// Shared write logic of the non-working periods (SPEC 44, data model 8): whole days, end on or after start, at most
/// 366 days, no overlap with another period of the same user. Existing activities are not touched; the response
/// lists the ones that fall in the period (<c>affectedEvents</c>) so the user decides.
/// </summary>
public sealed class CalendarNonWorkingPeriodWriter(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarCompanyGuard companyGuard,
    ICalendarRecurrenceExpander expander,
    ICalendarMapper mapper)
{
    /// <summary> Maximum length of a period, in days. </summary>
    public const int MaxDays = 366;

    /// <summary>
    /// Resolves the tenant and the calendar configuration (R1).
    /// </summary>
    public async Task<Response<CalendarCompanyContext>> OpenAsync(CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CalendarCompanyContext>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        return await companyGuard.GetContextAsync(currentUserService.CompanyId, cancellationToken);
    }

    /// <summary>
    /// The current user id, or <c>null</c>.
    /// </summary>
    public Guid? CurrentUserId => Guid.TryParse(currentUserService.UserId, out var id) ? id : null;

    /// <summary>
    /// Creates (<paramref name="existing"/> is <c>null</c>) or updates a period of <paramref name="userId"/>.
    /// </summary>
    public async Task<Response<CalendarNonWorkingPeriodResultDto>> SaveAsync(
        CalendarCompanyContext company,
        Guid userId,
        CalendarUserNonWorkingPeriod? existing,
        DateOnly startDate,
        DateOnly endDate,
        CalendarNonWorkingReason reason,
        string? notes,
        CancellationToken cancellationToken)
    {
        if (endDate < startDate || endDate.DayNumber - startDate.DayNumber + 1 > MaxDays)
        {
            return Response<CalendarNonWorkingPeriodResultDto>.Error(CalendarErrorCodes.InvalidEventTimes,
                [$"endDate must be on or after startDate and the period cannot exceed {MaxDays} days."]);
        }

        var repository = unitOfWork.GetRepository<CalendarUserNonWorkingPeriod>();
        var excludedId = existing?.Id ?? Guid.Empty;
        var overlaps = (await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == company.CompanyId
                && x.UserId == userId
                && x.GcRecord == 0
                && x.Id != excludedId
                && x.StartDate <= endDate
                && x.EndDate >= startDate))
            .Any();
        if (overlaps)
        {
            return Response<CalendarNonWorkingPeriodResultDto>.Error(CalendarErrorCodes.NonWorkingPeriodOverlap, ["The period overlaps another period of the user."]);
        }

        var entity = existing ?? new CalendarUserNonWorkingPeriod { CompanyId = company.CompanyId, UserId = userId };
        entity.StartDate = startDate;
        entity.EndDate = endDate;
        entity.Reason = reason;
        entity.Notes = CalendarCatalogRules.Clean(notes);

        if (existing is null)
        {
            await repository.InsertAsync(entity);
        }
        else
        {
            await repository.UpdateAsync(entity);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var fromUtc = CalendarTime.StartOfDayUtc(startDate, company.TimeZone);
        var toUtc = CalendarTime.StartOfDayUtc(endDate.AddDays(1), company.TimeZone);
        var rows = await unitOfWork.CalendarEvents.GetScheduleRowsAsync(company.CompanyId, userId, fromUtc, toUtc, cancellationToken);
        var affected = CalendarOccurrenceExpansion.Expand(rows, fromUtc, toUtc, company.TimeZoneId, expander)
            .Where(o => o.Source.BlocksTime)
            .Select(o => new CalendarAffectedEventDto(o.EventId, o.ExceptionId, o.Source.Title, o.StartUtc, o.EndUtc))
            .ToList();

        var user = await unitOfWork.GetRepository<ApplicationUser>().GetAsync(userId);
        var dto = mapper.ToDto(entity) with { UserName = user is null ? null : $"{user.FirstName} {user.LastName}".Trim() };

        return new Response<CalendarNonWorkingPeriodResultDto>
        {
            IsSuccess = true,
            Message = existing is null ? "Non-working period registered successfully." : "Non-working period updated successfully.",
            Data = new CalendarNonWorkingPeriodResultDto { Period = dto, AffectedEvents = affected }
        };
    }

    /// <summary>
    /// Logically deletes a period of the company, optionally restricted to one owner (<c>/me</c>).
    /// </summary>
    public async Task<Response<bool>> DeleteAsync(Guid companyId, Guid periodId, Guid? requiredUserId, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.GetRepository<CalendarUserNonWorkingPeriod>();
        var entity = await repository.GetAsync(periodId);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0 || (requiredUserId.HasValue && entity.UserId != requiredUserId))
        {
            return Response<bool>.Error(CalendarErrorCodes.NonWorkingPeriodNotFound, ["The non-working period was not found."]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<bool> { IsSuccess = true, Data = true, Message = "Non-working period deleted successfully." };
    }

    /// <summary>
    /// Whether the user has an active membership in the company.
    /// </summary>
    public async Task<bool> IsCompanyMemberAsync(Guid companyId, Guid userId) =>
        (await unitOfWork.GetRepository<UserCompany>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.UserId == userId && x.GcRecord == 0))
        .Any();

    /// <summary>
    /// Returns a tracked period of the company, or <c>null</c>.
    /// </summary>
    public async Task<CalendarUserNonWorkingPeriod?> FindAsync(Guid companyId, Guid periodId)
    {
        var entity = await unitOfWork.GetRepository<CalendarUserNonWorkingPeriod>().GetAsync(periodId);
        return entity is null || entity.CompanyId != companyId || entity.GcRecord != 0 ? null : entity;
    }
}
