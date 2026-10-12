using JOIN.Application.UnitTest.Common.TestDoubles;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.DeleteCalendarStatus;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.UpdateCalendarStatus;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarStatuses.Commands.UpdateCalendarStatus;

/// <summary>
/// Contains the unit tests for the global calendar status update command (SPEC 44 F10 "Catálogos").
/// Deactivating a system status (<see cref="DeleteCalendarStatusCommandHandler.SystemCodes"/>) is rejected.
/// </summary>
public sealed class UpdateCalendarStatusCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the system-status guard rejects deactivating PENDING (or any of the seeded statuses).
    /// </summary>
    [Fact]
    public async Task Handle_WhenDeactivatingSystemStatus_ShouldReturnStatusRequiredBySystemError()
    {
        // Arrange
        var context = new UpdateCalendarStatusCommandTestContext();
        var request = CreateValidCommand() with { IsActive = false };
        var entity = new CalendarStatus
        {
            Code = CalendarCodes.Statuses.Pending,
            GcRecord = 0,
            IsActive = true
        };
        EntityIdSetter.SetId(entity, request.Id);

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(entity);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_STATUS_REQUIRED_BY_SYSTEM");

        context.StatusRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarStatus>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: an existing status is mutated and persisted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldPersistChangesAndReturnDto()
    {
        // Arrange
        var context = new UpdateCalendarStatusCommandTestContext();
        var request = CreateValidCommand();
        var entity = new CalendarStatus
        {
            Code = "ON_HOLD",
            GcRecord = 0,
            IsActive = true,
            Name = "Old name"
        };
        EntityIdSetter.SetId(entity, request.Id);
        CalendarStatus? updated = null;

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(entity);
        context.StatusRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<CalendarStatus>()))
            .Callback<CalendarStatus>(s => updated = s)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Calendar status updated successfully.");
        response.Data.Should().NotBeNull();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be(request.Name.Trim());
        updated.Color.Should().Be(request.Color.Trim());
        updated.BlocksTime.Should().Be(request.BlocksTime);
        updated.IsFinal.Should().Be(request.IsFinal);
        updated.SortOrder.Should().Be(request.SortOrder);

        context.StatusRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarStatus>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private UpdateCalendarStatusCommand CreateValidCommand()
    {
        return new UpdateCalendarStatusCommand
        {
            Id = _fixture.Create<Guid>(),
            Name = "  Updated status  ",
            Description = "  Updated description  ",
            Color = "#FF7043",
            BlocksTime = false,
            IsFinal = true,
            IsActive = true,
            SortOrder = 60
        };
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the update handler tests.
    /// </summary>
    private sealed class UpdateCalendarStatusCommandTestContext
    {
        public UpdateCalendarStatusCommandTestContext()
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(_fixture.Create<Guid>());
            CurrentUserServiceMock.Setup(x => x.IsInRole(It.IsAny<string>())).Returns(false);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatus>()).Returns(StatusRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            MapperMock.Setup(x => x.ToDto(It.IsAny<CalendarStatus>())).Returns(new CalendarStatusDto());
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatus>> StatusRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ICalendarMapper> MapperMock { get; } = new();

        public UpdateCalendarStatusCommandHandler CreateHandler()
        {
            return new UpdateCalendarStatusCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                MapperMock.Object);
        }
    }
}