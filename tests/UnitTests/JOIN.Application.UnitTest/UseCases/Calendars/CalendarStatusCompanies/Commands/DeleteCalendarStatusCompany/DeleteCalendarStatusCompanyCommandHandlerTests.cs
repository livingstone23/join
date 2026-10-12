using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.DeleteCalendarStatusCompany;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarStatusCompanies.Commands.DeleteCalendarStatusCompany;

/// <summary>
/// Contains the unit tests for removing a calendar status from a company (SPEC 44 F10 "Catálogos").
/// System codes (PENDING/CANCELLED) and statuses that are still referenced cannot be removed.
/// </summary>
public sealed class DeleteCalendarStatusCompanyCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the system-code guard rejects removal of a company status whose global code is PENDING.
    /// </summary>
    [Fact]
    public async Task Handle_WhenGlobalCodeIsPending_ShouldReturnStatusRequiredBySystemError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteCalendarStatusCompanyCommandTestContext(companyId);
        var globalStatusId = _fixture.Create<Guid>();
        var companyStatusId = _fixture.Create<Guid>();

        var companyStatus = new CalendarStatusCompany
        {
            CompanyId = companyId,
            CalendarStatusId = globalStatusId,
            GcRecord = 0
        };
        EntityIdSetter.SetId(companyStatus, companyStatusId);
        var globalStatus = new CalendarStatus
        {
            Code = CalendarCodes.Statuses.Pending,
            GcRecord = 0,
            IsActive = true
        };
        EntityIdSetter.SetId(globalStatus, globalStatusId);

        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatusCompany>())
            .Returns(context.StatusCompanyRepositoryMock.Object);
        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatus>())
            .Returns(context.StatusRepositoryMock.Object);

        context.StatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(companyStatusId))
            .ReturnsAsync(companyStatus);
        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(globalStatusId))
            .ReturnsAsync(globalStatus);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteCalendarStatusCompanyCommand(companyStatusId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_STATUS_REQUIRED_BY_SYSTEM");

        context.StatusCompanyRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarStatusCompany>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the in-use guard rejects removal when a calendar event still points at the company status.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStatusHasEvents_ShouldReturnStatusInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteCalendarStatusCompanyCommandTestContext(companyId);
        var globalStatusId = _fixture.Create<Guid>();
        var companyStatusId = _fixture.Create<Guid>();

        var companyStatus = new CalendarStatusCompany
        {
            CompanyId = companyId,
            CalendarStatusId = globalStatusId,
            GcRecord = 0
        };
        EntityIdSetter.SetId(companyStatus, companyStatusId);
        var globalStatus = new CalendarStatus
        {
            Code = "ON_HOLD",
            GcRecord = 0,
            IsActive = true
        };
        EntityIdSetter.SetId(globalStatus, globalStatusId);

        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatusCompany>())
            .Returns(context.StatusCompanyRepositoryMock.Object);
        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatus>())
            .Returns(context.StatusRepositoryMock.Object);
        context.UnitOfWorkMock.Setup(x => x.CalendarEvents).Returns(context.CalendarEventsMock.Object);

        context.StatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(companyStatusId))
            .ReturnsAsync(companyStatus);
        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(globalStatusId))
            .ReturnsAsync(globalStatus);

        // No activity uses this status as initial or reschedule.
        context.UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarActivityCompany>());
        context.UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarCompany>());

        // The CalendarEvents.AnyAsync check finds at least one event.
        context.CalendarEventsMock
            .Setup(x => x.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CalendarEvent, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteCalendarStatusCompanyCommand(companyStatusId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_STATUS_IN_USE");

        context.StatusCompanyRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarStatusCompany>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the delete handler tests.
    /// </summary>
    private sealed class DeleteCalendarStatusCompanyCommandTestContext
    {
        public DeleteCalendarStatusCompanyCommandTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole(It.IsAny<string>())).Returns(false);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatusCompany>> StatusCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatus>> StatusRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<JOIN.Application.Interface.Persistence.Calendars.ICalendarEventRepository> CalendarEventsMock { get; } = new();

        public DeleteCalendarStatusCompanyCommandHandler CreateHandler()
        {
            return new DeleteCalendarStatusCompanyCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                new CalendarStatusCompanyRemovalGuard(UnitOfWorkMock.Object));
        }
    }
}