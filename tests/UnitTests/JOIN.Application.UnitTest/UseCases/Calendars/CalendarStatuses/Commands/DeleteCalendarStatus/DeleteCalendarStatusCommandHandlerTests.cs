using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.DeleteCalendarStatus;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarStatuses.Commands.DeleteCalendarStatus;

/// <summary>
/// Contains the unit tests for the global calendar status delete command (SPEC 44 F10 "Catálogos").
/// The five seeded statuses (PENDING/CONFIRMED/RESCHEDULED/CANCELLED/COMPLETED) cannot be deleted,
/// and a status enabled in any company cannot be deleted either.
/// </summary>
public sealed class DeleteCalendarStatusCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the system-code guard rejects deletion of any seeded status (CONFIRMED here).
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeIsSystemStatus_ShouldReturnStatusRequiredBySystemError()
    {
        // Arrange
        var context = new DeleteCalendarStatusCommandTestContext();
        var entity = new CalendarStatus
        {
            Code = CalendarCodes.Statuses.Confirmed,
            GcRecord = 0
        };
        EntityIdSetter.SetId(entity, _fixture.Create<Guid>());

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteCalendarStatusCommand(entity.Id), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_STATUS_REQUIRED_BY_SYSTEM");

        context.StatusRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarStatus>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the in-use guard rejects deletion when a company has the status enabled.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStatusEnabledInCompany_ShouldReturnStatusInUseError()
    {
        // Arrange
        var context = new DeleteCalendarStatusCommandTestContext();
        var entity = new CalendarStatus
        {
            Code = "ON_HOLD",
            GcRecord = 0
        };
        EntityIdSetter.SetId(entity, _fixture.Create<Guid>());

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.UnitOfWorkMock.SetupRepositoryRows(
        [
            new CalendarStatusCompany { CalendarStatusId = entity.Id, CompanyId = _fixture.Create<Guid>(), GcRecord = 0 }
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteCalendarStatusCommand(entity.Id), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_STATUS_IN_USE");

        context.StatusRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarStatus>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the delete handler tests.
    /// </summary>
    private sealed class DeleteCalendarStatusCommandTestContext
    {
        public DeleteCalendarStatusCommandTestContext()
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(_fixture.Create<Guid>());
            CurrentUserServiceMock.Setup(x => x.IsInRole(It.IsAny<string>())).Returns(false);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatus>()).Returns(StatusRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatus>> StatusRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public DeleteCalendarStatusCommandHandler CreateHandler()
        {
            return new DeleteCalendarStatusCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}