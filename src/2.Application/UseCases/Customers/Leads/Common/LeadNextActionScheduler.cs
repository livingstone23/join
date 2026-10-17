using JOIN.Application.Common;
using JOIN.Application.Common.Settings;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Customers.Leads;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Customers.Leads.Common;

/// <summary>
/// Schedules the next action of a lead contact as a calendar event in the lead owner's calendar (SPEC 65 A.3), with the
/// calendar activity of the contact type and the existing calendar rules (SPEC 44-post: business hours, overlap). The
/// endpoint belongs to the <c>Leads</c> resource, so the Calendar module gate (SPEC 48-post) is checked here.
/// </summary>
public sealed class LeadNextActionScheduler(
    IUnitOfWork unitOfWork,
    IOptions<ModuleEnforcementSettings> moduleSettings,
    IPermissionService permissionService,
    ICurrentUserService currentUserService,
    CalendarEventCommandProcessor calendar)
{
    /// <summary>Permission resource of the own calendar (option <c>MyCalendar</c>), owned by the Calendar module.</summary>
    public const string CalendarResource = "Calendar";

    public async Task<(Guid? EventId, Response<T>? Error)> ScheduleAsync<T>(Lead lead, LeadNextActionInput input, CancellationToken cancellationToken)
    {
        // Same rule as the SPEC 48-post filter: the SuperAdmin is never blocked by a module.
        if (moduleSettings.Value.EnforceCompanyModules && !currentUserService.IsInRole("SuperAdmin"))
        {
            var access = await permissionService.GetModuleAccessAsync(lead.CompanyId, CalendarResource, cancellationToken);
            if (!access.IsEnabled)
            {
                return (null, Response<T>.Error(LeadErrorCodes.ModuleNotEnabled, [access.ModuleName ?? "Calendar"]));
            }
        }

        var copy = (await unitOfWork.GetRepository<ContactTypeCompany>().GetAllIncludingDeletedAsync(c =>
                c.Id == input.ContactTypeCompanyId && c.CompanyId == lead.CompanyId && c.GcRecord == 0 && c.IsActive))
            .FirstOrDefault();
        var master = copy is null
            ? null
            : (await unitOfWork.GetRepository<ContactType>().GetAllIncludingDeletedAsync(t => t.Id == copy.ContactTypeId && t.GcRecord == 0 && t.IsActive)).FirstOrDefault();
        if (copy?.CalendarActivityCompanyId is not { } activityId || master is not { SchedulesCalendarEvent: true })
        {
            return (null, Response<T>.Error(LeadErrorCodes.ContactTypeNotSchedulable, ["The contact type does not schedule calendar events or has no calendar activity."]));
        }

        var created = await calendar.CreateAsync(new NextActionEvent
        {
            CalendarActivityCompanyId = activityId,
            Title = string.IsNullOrWhiteSpace(input.Title) ? $"{lead.Code} · {lead.Topic}" : input.Title.Trim(),
            Location = input.Location,
            VideoCallUrl = input.VideoCallUrl,
            Start = new DateTimeOffset(DateTime.SpecifyKind(input.StartUtc, DateTimeKind.Utc)),
            End = new DateTimeOffset(DateTime.SpecifyKind(input.EndUtc, DateTimeKind.Utc)),
            CustomerId = lead.CustomerId
        }, lead.OwnerUserId, cancellationToken);
        if (!created.IsSuccess)
        {
            return (null, Response<T>.Error(created.Message, created.Errors?.ToList() ?? []));
        }

        var calendarEvent = await unitOfWork.CalendarEvents.GetAsync(created.Data!.Id);
        calendarEvent!.LeadId = lead.Id;
        await unitOfWork.CalendarEvents.UpdateAsync(calendarEvent);
        return (calendarEvent.Id, null);
    }

    private sealed record NextActionEvent : ICalendarEventInput
    {
        public Guid? CalendarId { get; init; }
        public Guid CalendarActivityCompanyId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? Summary { get; init; }
        public string? Description { get; init; }
        public string? Location { get; init; }
        public string? VideoCallUrl { get; init; }
        public bool IsAllDay { get; init; }
        public DateTimeOffset? Start { get; init; }
        public DateTimeOffset? End { get; init; }
        public DateOnly? StartDate { get; init; }
        public DateOnly? EndDate { get; init; }
        public string? RecurrenceRule { get; init; }
        public Guid? RequesterPersonId { get; init; }
        public Guid? CustomerId { get; init; }
        public Guid? TicketId { get; init; }
        public Guid? ProjectId { get; init; }
    }
}
