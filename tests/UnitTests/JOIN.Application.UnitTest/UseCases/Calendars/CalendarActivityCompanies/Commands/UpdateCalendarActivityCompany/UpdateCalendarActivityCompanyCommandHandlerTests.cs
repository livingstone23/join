using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.UpdateCalendarActivityCompany;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarActivityCompanies.Commands.UpdateCalendarActivityCompany;

/// <summary>
/// Contains the unit tests for updating a calendar activity type of a company (SPEC 44 F10 "Catálogos").
/// </summary>
public sealed class UpdateCalendarActivityCompanyCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    private static CalendarActivity BuildCalendarActivity(Guid id, string code, string name)
    {
        var activity = new CalendarActivity { Code = code, Name = name, GcRecord = 0, IsActive = true };
        EntityIdSetter.SetId(activity, id);
        return activity;
    }

    private static CalendarStatus BuildCalendarStatus(Guid id, string code)
    {
        var status = new CalendarStatus { Code = code, GcRecord = 0, IsActive = true };
        EntityIdSetter.SetId(status, id);
        return status;
    }

    /// <summary>
    /// Verifies the validation branch when the requested initial status is not usable in the company
    /// (inactive or otherwise failing the "usable" rule).
    /// </summary>
    [Fact]
    public async Task Handle_WhenInitialStatusIsNotUsable_ShouldReturnStatusNotEnabledError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var globalId = _fixture.Create<Guid>();
        var initialStatusId = _fixture.Create<Guid>();
        var context = new UpdateCalendarActivityCompanyCommandTestContext(companyId);
        var request = new UpdateCalendarActivityCompanyCommand
        {
            Name = "Meeting (company)",
            Color = "#FF7043",
            DefaultDurationMinutes = 45,
            AllowsOverlap = false,
            RequiresBusinessHours = true,
            InitialCalendarStatusCompanyId = initialStatusId,
            IsActive = true,
            SortOrder = 10
        };
        EntityIdSetter.SetId(request, _fixture.Create<Guid>());

        // Existing company activity row that matches the request id.
        var companyRow = new CalendarActivityCompany
        {
            CompanyId = companyId,
            CalendarActivityId = globalId,
            GcRecord = 0
        };
        EntityIdSetter.SetId(companyRow, request.Id);

        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivityCompany>())
            .Returns(context.ActivityCompanyRepositoryMock.Object);
        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatusCompany>())
            .Returns(context.StatusCompanyRepositoryMock.Object);

        context.ActivityCompanyRepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(companyRow);

        // The requested initial status is inactive (fails the "usable" rule).
        var inactiveStatus = new CalendarStatusCompany
        {
            CompanyId = companyId,
            CalendarStatusId = _fixture.Create<Guid>(),
            IsActive = false,
            GcRecord = 0
        };
        EntityIdSetter.SetId(inactiveStatus, initialStatusId);
        context.StatusCompanyRepositoryMock.SetupRows(Array.Empty<CalendarStatusCompany>());
        context.StatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(initialStatusId))
            .ReturnsAsync(inactiveStatus);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_STATUS_NOT_ENABLED");

        context.ActivityCompanyRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarActivityCompany>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: an existing activity type is updated and persisted with the
    /// company-effective values.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateActivityAndReturnDto()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var globalId = _fixture.Create<Guid>();
        var initialStatusId = _fixture.Create<Guid>();
        var context = new UpdateCalendarActivityCompanyCommandTestContext(companyId);
        var request = new UpdateCalendarActivityCompanyCommand
        {
            Name = "  Updated meeting  ",
            Color = "  #FF7043 ",
            DefaultDurationMinutes = 60,
            AllowsOverlap = true,
            RequiresBusinessHours = false,
            InitialCalendarStatusCompanyId = initialStatusId,
            IsActive = true,
            SortOrder = 15
        };
        EntityIdSetter.SetId(request, _fixture.Create<Guid>());

        var companyRow = new CalendarActivityCompany
        {
            CompanyId = companyId,
            CalendarActivityId = globalId,
            GcRecord = 0,
            IsActive = false
        };
        EntityIdSetter.SetId(companyRow, request.Id);
        CalendarActivityCompany? updated = null;

        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivityCompany>())
            .Returns(context.ActivityCompanyRepositoryMock.Object);
        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatusCompany>())
            .Returns(context.StatusCompanyRepositoryMock.Object);
        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivity>())
            .Returns(context.ActivityRepositoryMock.Object);

        context.ActivityCompanyRepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(companyRow);
        context.ActivityCompanyRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<CalendarActivityCompany>()))
            .Callback<CalendarActivityCompany>(entity => updated = entity)
            .ReturnsAsync(true);

        // Active initial status of the same company (passes the "usable" rule).
        var pendingGlobalId = _fixture.Create<Guid>();
        var usableStatus = new CalendarStatusCompany
        {
            CompanyId = companyId,
            CalendarStatusId = pendingGlobalId,
            IsActive = true,
            GcRecord = 0
        };
        EntityIdSetter.SetId(usableStatus, initialStatusId);
        context.StatusCompanyRepositoryMock.SetupRows(
        [
            usableStatus
        ]);
        context.StatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(initialStatusId))
            .ReturnsAsync(usableStatus);

        context.ActivityRepositoryMock
            .Setup(x => x.GetAsync(globalId))
            .ReturnsAsync(BuildCalendarActivity(globalId, "MEETING", "Meeting"));

        // The calendar catalog reader's "usable" check fetches the global status of the company status.
        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatus>())
            .Returns(context.StatusRepositoryMock.Object);
        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(pendingGlobalId))
            .ReturnsAsync(BuildCalendarStatus(pendingGlobalId, "PENDING"));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Calendar activity type updated successfully.");
        response.Data.Should().NotBeNull();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be(request.Name.Trim());
        updated.Color.Should().Be(request.Color.Trim());
        updated.DefaultDurationMinutes.Should().Be(60);
        updated.AllowsOverlap.Should().BeTrue();
        updated.IsActive.Should().BeTrue();
        updated.InitialCalendarStatusCompanyId.Should().Be(initialStatusId);
        updated.SortOrder.Should().Be(15);

        context.ActivityCompanyRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarActivityCompany>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the update handler tests.
    /// </summary>
    private sealed class UpdateCalendarActivityCompanyCommandTestContext
    {
        public UpdateCalendarActivityCompanyCommandTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole(It.IsAny<string>())).Returns(false);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarActivityCompany>> ActivityCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarActivity>> ActivityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatusCompany>> StatusCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatus>> StatusRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public UpdateCalendarActivityCompanyCommandHandler CreateHandler()
        {
            return new UpdateCalendarActivityCompanyCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                new CalendarCatalogReader(UnitOfWorkMock.Object));
        }
    }
}