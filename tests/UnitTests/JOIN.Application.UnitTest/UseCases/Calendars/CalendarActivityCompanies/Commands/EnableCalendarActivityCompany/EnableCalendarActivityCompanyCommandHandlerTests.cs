using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.EnableCalendarActivityCompany;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarActivityCompanies.Commands.EnableCalendarActivityCompany;

/// <summary>
/// Contains the unit tests for enabling a global calendar activity type in the current company
/// (SPEC 44 F10 "Catálogos"). The handler copies the global values and rejects duplicates.
/// </summary>
public sealed class EnableCalendarActivityCompanyCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    private static CalendarActivity BuildCalendarActivity(
        Guid id,
        string code = "MEETING",
        string name = "Meeting",
        string color = "#1E88E5",
        int defaultDurationMinutes = 30,
        bool allowsOverlap = false,
        bool requiresBusinessHours = true,
        int sortOrder = 5)
    {
        var activity = new CalendarActivity
        {
            Code = code,
            Name = name,
            Color = color,
            DefaultDurationMinutes = defaultDurationMinutes,
            AllowsOverlap = allowsOverlap,
            RequiresBusinessHours = requiresBusinessHours,
            IsActive = true,
            SortOrder = sortOrder,
            GcRecord = 0
        };
        EntityIdSetter.SetId(activity, id);
        return activity;
    }

    private static CalendarStatus BuildPendingGlobalStatus(Guid id)
    {
        var status = new CalendarStatus
        {
            Code = CalendarCodes.Statuses.Pending,
            GcRecord = 0,
            IsActive = true
        };
        EntityIdSetter.SetId(status, id);
        return status;
    }

    private static CalendarStatusCompany BuildPendingCompanyStatus(Guid id, Guid companyId, Guid globalId)
    {
        var status = new CalendarStatusCompany
        {
            CalendarStatusId = globalId,
            CompanyId = companyId,
            IsActive = true,
            GcRecord = 0
        };
        EntityIdSetter.SetId(status, id);
        return status;
    }

    /// <summary>
    /// Verifies the duplicate-guard rejects an attempt to enable the same type twice in the company.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAlreadyEnabled_ShouldReturnActivityAlreadyEnabledError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var globalId = _fixture.Create<Guid>();
        var context = new EnableCalendarActivityCompanyCommandTestContext(companyId);
        var request = new EnableCalendarActivityCompanyCommand
        {
            CalendarActivityId = globalId
        };

        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivity>())
            .Returns(context.ActivityRepositoryMock.Object);
        context.ActivityRepositoryMock
            .Setup(x => x.GetAsync(globalId))
            .ReturnsAsync(BuildCalendarActivity(globalId, code: string.Empty, name: string.Empty, color: string.Empty, defaultDurationMinutes: 0, allowsOverlap: false, requiresBusinessHours: false, sortOrder: 0));

        context.UnitOfWorkMock.SetupRepositoryRows(
        [
            new CalendarActivityCompany { CalendarActivityId = globalId, CompanyId = companyId, GcRecord = 0 }
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_ACTIVITY_ALREADY_ENABLED");

        context.ActivityCompanyRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarActivityCompany>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: the global values are copied into the company row, including the
    /// default initial status (<c>PENDING</c>) when none is provided.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCopyGlobalValuesAndInsertCompanyRow()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var globalId = _fixture.Create<Guid>();
        var pendingGlobalId = _fixture.Create<Guid>();
        var pendingCompanyId = _fixture.Create<Guid>();
        var context = new EnableCalendarActivityCompanyCommandTestContext(companyId);
        var request = new EnableCalendarActivityCompanyCommand
        {
            CalendarActivityId = globalId
        };
        CalendarActivityCompany? inserted = null;

        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivity>())
            .Returns(context.ActivityRepositoryMock.Object);
        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatus>())
            .Returns(context.StatusRepositoryMock.Object);
        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatusCompany>())
            .Returns(context.StatusCompanyRepositoryMock.Object);
        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivityCompany>())
            .Returns(context.ActivityCompanyRepositoryMock.Object);

        context.ActivityRepositoryMock
            .Setup(x => x.GetAsync(globalId))
            .ReturnsAsync(BuildCalendarActivity(globalId));

        // Empty activity company rows (not already enabled).
        context.ActivityCompanyRepositoryMock.SetupRows(Array.Empty<CalendarActivityCompany>());

        // PENDING global status -> used as default initial status.
        context.StatusRepositoryMock.SetupRows(
        [
            BuildPendingGlobalStatus(pendingGlobalId)
        ]);
        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(pendingGlobalId))
            .ReturnsAsync(BuildPendingGlobalStatus(pendingGlobalId));

        context.StatusCompanyRepositoryMock.SetupRows(
        [
            BuildPendingCompanyStatus(pendingCompanyId, companyId, pendingGlobalId)
        ]);

        context.StatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(pendingCompanyId))
            .ReturnsAsync(BuildPendingCompanyStatus(pendingCompanyId, companyId, pendingGlobalId));

        context.ActivityCompanyRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarActivityCompany>()))
            .Callback<CalendarActivityCompany>(entity => inserted = entity)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Calendar activity type enabled successfully.");
        response.Data.Should().NotBeNull();
        inserted.Should().NotBeNull();
        inserted!.CompanyId.Should().Be(companyId);
        inserted.CalendarActivityId.Should().Be(globalId);
        inserted.Name.Should().Be("Meeting");
        inserted.Color.Should().Be("#1E88E5");
        inserted.DefaultDurationMinutes.Should().Be(30);
        inserted.AllowsOverlap.Should().BeFalse();
        inserted.RequiresBusinessHours.Should().BeTrue();
        inserted.InitialCalendarStatusCompanyId.Should().Be(pendingCompanyId);
        inserted.IsActive.Should().BeTrue();
        inserted.SortOrder.Should().Be(5);

        context.ActivityCompanyRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarActivityCompany>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the enable handler tests.
    /// </summary>
    private sealed class EnableCalendarActivityCompanyCommandTestContext
    {
        public EnableCalendarActivityCompanyCommandTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole(It.IsAny<string>())).Returns(false);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarActivity>> ActivityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarActivityCompany>> ActivityCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatus>> StatusRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatusCompany>> StatusCompanyRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public EnableCalendarActivityCompanyCommandHandler CreateHandler()
        {
            return new EnableCalendarActivityCompanyCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                new CalendarCatalogReader(UnitOfWorkMock.Object));
        }
    }
}