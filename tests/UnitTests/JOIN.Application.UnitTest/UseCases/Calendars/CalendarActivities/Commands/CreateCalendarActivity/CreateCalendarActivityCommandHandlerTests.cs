using AutoFixture;
using FluentAssertions;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.CreateCalendarActivity;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarActivities.Commands.CreateCalendarActivity;

/// <summary>
/// Contains the unit tests for the global calendar activity type creation command (SPEC 44 F10 "Catálogos").
/// </summary>
public sealed class CreateCalendarActivityCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the duplicate-code guard rejects another active activity type that already uses the same code.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeAlreadyExists_ShouldReturnActivityCodeDuplicateError()
    {
        // Arrange
        var context = new CreateCalendarActivityCommandTestContext();
        var request = CreateValidCommand();
        var normalizedCode = CalendarCatalogRules.NormalizeCode(request.Code);

        context.ActivityRepositoryMock.SetupRows(
        [
            new CalendarActivity { Code = normalizedCode, GcRecord = 0 }
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_ACTIVITY_CODE_DUPLICATE");

        context.ActivityRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarActivity>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: a new activity type is inserted and the DTO is returned.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldInsertActivityAndReturnDto()
    {
        // Arrange
        var context = new CreateCalendarActivityCommandTestContext();
        var request = CreateValidCommand();
        CalendarActivity? inserted = null;

        context.ActivityRepositoryMock.SetupRows(Array.Empty<CalendarActivity>());
        context.ActivityRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarActivity>()))
            .Callback<CalendarActivity>(entity => inserted = entity)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Calendar activity type created successfully.");
        response.Data.Should().NotBeNull();
        inserted.Should().NotBeNull();
        inserted!.Code.Should().Be(CalendarCatalogRules.NormalizeCode(request.Code));
        inserted.Name.Should().Be(request.Name.Trim());
        inserted.Description.Should().Be(CalendarCatalogRules.Clean(request.Description));
        inserted.Color.Should().Be(request.Color.Trim());
        inserted.DefaultDurationMinutes.Should().Be(request.DefaultDurationMinutes);
        inserted.IsActive.Should().BeTrue();

        context.ActivityRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarActivity>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private CreateCalendarActivityCommand CreateValidCommand()
    {
        return new CreateCalendarActivityCommand
        {
            Code = "REVIEW_CALL",
            Name = "  Review call  ",
            Description = "  Periodic review  ",
            Color = "#1E88E5",
            Icon = "phone",
            DefaultDurationMinutes = 30,
            AllowsOverlap = false,
            RequiresBusinessHours = true,
            IsActive = true,
            SortOrder = 10
        };
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the create handler tests.
    /// </summary>
    private sealed class CreateCalendarActivityCommandTestContext
    {
        public CreateCalendarActivityCommandTestContext()
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

        public CreateCalendarActivityCommandHandler CreateHandler()
        {
            return new CreateCalendarActivityCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                MapperMock.Object);
        }
    }
}