using JOIN.Application.UnitTest.Common.TestDoubles;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.UpdateCalendarActivity;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarActivities.Commands.UpdateCalendarActivity;

/// <summary>
/// Contains the unit tests for the global calendar activity type update command (SPEC 44 F10 "Catálogos").
/// </summary>
public sealed class UpdateCalendarActivityCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the activity type does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActivityDoesNotExist_ShouldReturnActivityNotFoundError()
    {
        // Arrange
        var context = new UpdateCalendarActivityCommandTestContext();
        var request = CreateValidCommand();

        context.ActivityRepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync((CalendarActivity?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_ACTIVITY_NOT_FOUND");

        context.ActivityRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarActivity>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: an existing activity type is mutated and persisted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActivityExists_ShouldPersistChangesAndReturnDto()
    {
        // Arrange
        var context = new UpdateCalendarActivityCommandTestContext();
        var request = CreateValidCommand();
        var entity = new CalendarActivity
        {
            Code = "MEETING",
            Name = "Old name"
        };
        EntityIdSetter.SetId(entity, request.Id);
        CalendarActivity? updated = null;

        context.ActivityRepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(entity);
        context.ActivityRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<CalendarActivity>()))
            .Callback<CalendarActivity>(e => updated = e)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Calendar activity type updated successfully.");
        updated.Should().NotBeNull();
        updated!.Name.Should().Be(request.Name.Trim());
        updated.Color.Should().Be(request.Color.Trim());
        updated.DefaultDurationMinutes.Should().Be(request.DefaultDurationMinutes);
        updated.AllowsOverlap.Should().Be(request.AllowsOverlap);
        updated.SortOrder.Should().Be(request.SortOrder);

        context.ActivityRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarActivity>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private UpdateCalendarActivityCommand CreateValidCommand()
    {
        return new UpdateCalendarActivityCommand
        {
            Id = _fixture.Create<Guid>(),
            Name = "  Updated review  ",
            Description = "  Updated description  ",
            Color = "#FF7043",
            Icon = "calendar",
            DefaultDurationMinutes = 45,
            AllowsOverlap = true,
            RequiresBusinessHours = false,
            IsActive = true,
            SortOrder = 20
        };
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the update handler tests.
    /// </summary>
    private sealed class UpdateCalendarActivityCommandTestContext
    {
        public UpdateCalendarActivityCommandTestContext()
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(_fixture.Create<Guid>());
            CurrentUserServiceMock.Setup(x => x.IsInRole(It.IsAny<string>())).Returns(false);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivity>()).Returns(ActivityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            MapperMock.Setup(x => x.ToDto(It.IsAny<CalendarActivity>())).Returns(new CalendarActivityDto());
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarActivity>> ActivityRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ICalendarMapper> MapperMock { get; } = new();

        public UpdateCalendarActivityCommandHandler CreateHandler()
        {
            return new UpdateCalendarActivityCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                MapperMock.Object);
        }
    }
}