using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.DeleteCalendarActivity;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarActivities.Commands.DeleteCalendarActivity;

/// <summary>
/// Contains the unit tests for the global calendar activity type delete command (SPEC 44 F10 "Catálogos").
/// PERSON_REVIEW is required by the channel agent (SPEC 45); a type enabled in any company cannot be deleted.
/// </summary>
public sealed class DeleteCalendarActivityCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the system-code guard rejects deletion of PERSON_REVIEW.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeIsPersonReview_ShouldReturnActivityRequiredBySystemError()
    {
        // Arrange
        var context = new DeleteCalendarActivityCommandTestContext();
        var entity = new CalendarActivity
        {
            Code = CalendarCodes.Activities.PersonReview,
            GcRecord = 0
        };
        EntityIdSetter.SetId(entity, _fixture.Create<Guid>());

        context.ActivityRepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteCalendarActivityCommand(entity.Id), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_ACTIVITY_REQUIRED_BY_SYSTEM");

        context.ActivityRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarActivity>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the in-use guard rejects deletion when a company has the activity type enabled.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActivityEnabledInCompany_ShouldReturnActivityInUseError()
    {
        // Arrange
        var context = new DeleteCalendarActivityCommandTestContext();
        var entity = new CalendarActivity
        {
            Code = "CUSTOM_TYPE",
            GcRecord = 0
        };
        EntityIdSetter.SetId(entity, _fixture.Create<Guid>());

        context.ActivityRepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.UnitOfWorkMock.SetupRepositoryRows(
        [
            new CalendarActivityCompany { CalendarActivityId = entity.Id, CompanyId = _fixture.Create<Guid>(), GcRecord = 0 }
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteCalendarActivityCommand(entity.Id), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_ACTIVITY_IN_USE");

        context.ActivityRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarActivity>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the delete handler tests.
    /// </summary>
    private sealed class DeleteCalendarActivityCommandTestContext
    {
        public DeleteCalendarActivityCommandTestContext()
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(_fixture.Create<Guid>());
            CurrentUserServiceMock.Setup(x => x.IsInRole(It.IsAny<string>())).Returns(false);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivity>()).Returns(ActivityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarActivity>> ActivityRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public DeleteCalendarActivityCommandHandler CreateHandler()
        {
            return new DeleteCalendarActivityCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}