using System.Linq.Expressions;
using AutoFixture;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarActivityIndex;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Domain.Calendars;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using Moq;
using CalendarEvent = JOIN.Domain.Calendars.CalendarEvent;
using CalendarAttachmentSettings = JOIN.Domain.Calendars.CalendarAttachmentSettings;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEventRecords;

/// <summary>
/// Shared wiring for SPEC 69 handler tests: <see cref="IUnitOfWork"/> with the calendars repository graph
/// (<see cref="CalendarEvent"/>, <see cref="CalendarEventComment"/>, <see cref="CalendarEventDocument"/>,
/// <see cref="CalendarAttachmentSettings"/>, <see cref="CalendarUserConfiguration"/>,
/// <see cref="CommunicationChannel"/>, <see cref="CalendarEventLog"/>), a real
/// <see cref="CalendarActivityVisibility"/> wired to the same mocks, and a current user service that maps to
/// the test's <c>ActorUserId</c>. Tests that need a different visibility verdict override
/// <see cref="ConfigureVisibility"/>.
/// </summary>
internal sealed class Spec69CalendarEventRecordsContext
{
    public Spec69CalendarEventRecordsContext(bool ownerCanSeeEvent = true, bool canViewOthers = false)
    {
        CompanyId = Guid.NewGuid();
        ActorUserId = Guid.NewGuid();
        WebChannelId = Guid.NewGuid();
        CalendarId = Guid.NewGuid();
        EventId = Guid.NewGuid();

        CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(CompanyId);
        CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(ActorUserId.ToString());

        var configuration = new CalendarUserConfiguration
        {
            CompanyId = CompanyId,
            UserId = ActorUserId,
            DefaultCalendarId = CalendarId,
            CanViewOthersActivities = canViewOthers
        };
        CalendarUserConfigurationRepositoryMock.SetupRows(new[] { configuration });

        WebChannel = new CommunicationChannel
        {
            Code = CommunicationChannelCodes.Web,
            Name = "Web",
            IsActive = true
        };
        EntityIdSetter.SetId(WebChannel, WebChannelId);
        CommunicationChannelRepositoryMock.SetupRows(new[] { WebChannel });
        CommunicationChannelRepositoryMock.Setup(x => x.GetAsync(WebChannelId)).ReturnsAsync(WebChannel);

        // Visibility — the event is loaded from CalendarEventsRepositoryMock below.
        CalendarEvent = new CalendarEvent
        {
            CompanyId = CompanyId,
            OwnerUserId = ActorUserId,
            CalendarId = CalendarId,
            CalendarActivityCompanyId = Guid.NewGuid(),
            CalendarStatusCompanyId = Guid.NewGuid(),
            OriginChannelId = WebChannelId,
            Title = "Sample",
            StartUtc = DateTime.SpecifyKind(new DateTime(2027, 4, 5, 16, 0, 0), DateTimeKind.Utc),
            EndUtc = DateTime.SpecifyKind(new DateTime(2027, 4, 5, 17, 0, 0), DateTimeKind.Utc)
        };
        EntityIdSetter.SetId(CalendarEvent, EventId);

        UnitOfWorkMock.Setup(x => x.CalendarEvents).Returns(CalendarEventsRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<CalendarUserConfiguration>())
            .Returns(CalendarUserConfigurationRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<CommunicationChannel>())
            .Returns(CommunicationChannelRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<CalendarEventComment>())
            .Returns(CalendarEventCommentRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<CalendarEventDocument>())
            .Returns(CalendarEventDocumentRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<CalendarAttachmentSettings>())
            .Returns(CalendarAttachmentSettingsRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<CalendarEventLog>())
            .Returns(CalendarEventLogRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationUser>())
            .Returns(ApplicationUserRepositoryMock.Object);

        ConfigureVisibility(ownerCanSeeEvent);

        // Reusable visibility instance shared by every handler.
        Visibility = new CalendarActivityVisibility(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }

    public Guid CompanyId { get; }
    public Guid ActorUserId { get; }
    public Guid WebChannelId { get; }
    public Guid CalendarId { get; }
    public Guid EventId { get; }
    public CalendarEvent CalendarEvent { get; }
    public CommunicationChannel WebChannel { get; }

    public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
    public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
    public Mock<ICalendarEventRepository> CalendarEventsRepositoryMock { get; } = new();
    public Mock<IGenericRepository<CalendarUserConfiguration>> CalendarUserConfigurationRepositoryMock { get; } = new();
    public Mock<IGenericRepository<CommunicationChannel>> CommunicationChannelRepositoryMock { get; } = new();
    public Mock<IGenericRepository<CalendarEventComment>> CalendarEventCommentRepositoryMock { get; } = new();
    public Mock<IGenericRepository<CalendarEventDocument>> CalendarEventDocumentRepositoryMock { get; } = new();
    public Mock<IGenericRepository<JOIN.Domain.Calendars.CalendarAttachmentSettings>> CalendarAttachmentSettingsRepositoryMock { get; } = new();
    public Mock<IGenericRepository<CalendarEventLog>> CalendarEventLogRepositoryMock { get; } = new();
    public Mock<IGenericRepository<ApplicationUser>> ApplicationUserRepositoryMock { get; } = new();

    public CalendarActivityVisibility Visibility { get; }

    /// <summary> Capture insert / update / delete calls so handlers can be asserted in tests. </summary>
    public List<CalendarEventComment> CapturedCommentInserts { get; } = new();
    public List<CalendarEventDocument> CapturedDocumentInserts { get; } = new();
    public List<CalendarEventComment> CapturedCommentUpdates { get; } = new();
    public List<CalendarEventDocument> CapturedDocumentUpdates { get; } = new();
    public List<CalendarEventLog> CapturedLogs { get; } = new();

    /// <summary>
    /// Hooks the comment / document / log repositories so handler writes are captured for assertions.
    /// </summary>
    public void TrackWrites()
    {
        CalendarEventCommentRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarEventComment>()))
            .Callback<CalendarEventComment>(c =>
            {
                EntityIdSetter.SetId(c, Guid.NewGuid());
                CapturedCommentInserts.Add(c);
            })
            .ReturnsAsync(true);
        CalendarEventCommentRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<CalendarEventComment>()))
            .Callback<CalendarEventComment>(c => CapturedCommentUpdates.Add(c))
            .ReturnsAsync(true);
        CalendarEventDocumentRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarEventDocument>()))
            .Callback<CalendarEventDocument>(d =>
            {
                EntityIdSetter.SetId(d, Guid.NewGuid());
                CapturedDocumentInserts.Add(d);
            })
            .ReturnsAsync(true);
        CalendarEventDocumentRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<CalendarEventDocument>()))
            .Callback<CalendarEventDocument>(d => CapturedDocumentUpdates.Add(d))
            .ReturnsAsync(true);
        CalendarEventLogRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarEventLog>()))
            .Callback<CalendarEventLog>(log => CapturedLogs.Add(log))
            .ReturnsAsync(true);
    }

    /// <summary>
    /// Configures the calendar event visibility: the event either exists and is visible, or it does not
    /// exist (foreign-tenant / soft-deleted).
    /// </summary>
    private void ConfigureVisibility(bool ownerCanSeeEvent)
    {
        if (ownerCanSeeEvent)
        {
            CalendarEventsRepositoryMock.Setup(x => x.GetAsync(EventId)).ReturnsAsync(CalendarEvent);
        }
        else
        {
            CalendarEventsRepositoryMock.Setup(x => x.GetAsync(EventId)).ReturnsAsync((CalendarEvent?)null);
        }
    }
}

/// <summary> Builds a <see cref="CommunicationChannel"/> for tests that need one outside the context. </summary>
internal static class Spec69Helpers
{
    public static CommunicationChannel NewWebChannel() => new()
    {
        Code = CommunicationChannelCodes.Web,
        Name = "Web",
        IsActive = true
    };
}