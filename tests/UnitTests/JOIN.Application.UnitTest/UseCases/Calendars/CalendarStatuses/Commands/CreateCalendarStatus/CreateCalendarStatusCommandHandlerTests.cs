using AutoFixture;
using FluentAssertions;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.CreateCalendarStatus;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarStatuses.Commands.CreateCalendarStatus;

/// <summary>
/// Contains the unit tests for the global calendar status creation command (SPEC 44 F10 "Catálogos").
/// </summary>
public sealed class CreateCalendarStatusCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the duplicate-code guard rejects another active status that already uses the same code.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeAlreadyExists_ShouldReturnStatusCodeDuplicateError()
    {
        // Arrange
        var context = new CreateCalendarStatusCommandTestContext();
        var request = CreateValidCommand();
        var normalizedCode = CalendarCatalogRules.NormalizeCode(request.Code);

        context.StatusRepositoryMock.SetupRows(
        [
            new CalendarStatus { Code = normalizedCode, GcRecord = 0 }
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_STATUS_CODE_DUPLICATE");

        context.StatusRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarStatus>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: a new status is inserted and the DTO is returned.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldInsertStatusAndReturnDto()
    {
        // Arrange
        var context = new CreateCalendarStatusCommandTestContext();
        var request = CreateValidCommand();
        CalendarStatus? inserted = null;

        context.StatusRepositoryMock.SetupRows(Array.Empty<CalendarStatus>());
        context.StatusRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarStatus>()))
            .Callback<CalendarStatus>(entity => inserted = entity)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Calendar status created successfully.");
        response.Data.Should().NotBeNull();
        inserted.Should().NotBeNull();
        inserted!.Code.Should().Be(CalendarCatalogRules.NormalizeCode(request.Code));
        inserted.Name.Should().Be(request.Name.Trim());
        inserted.Color.Should().Be(request.Color.Trim());
        inserted.BlocksTime.Should().BeTrue();
        inserted.IsFinal.Should().BeFalse();
        inserted.IsActive.Should().BeTrue();

        context.StatusRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarStatus>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private CreateCalendarStatusCommand CreateValidCommand()
    {
        return new CreateCalendarStatusCommand
        {
            Code = "ON_HOLD",
            Name = "  On hold  ",
            Description = "  Awaiting response  ",
            Color = "#FFB300",
            BlocksTime = true,
            IsFinal = false,
            IsActive = true,
            SortOrder = 50
        };
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the create handler tests.
    /// </summary>
    private sealed class CreateCalendarStatusCommandTestContext
    {
        public CreateCalendarStatusCommandTestContext()
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

        public CreateCalendarStatusCommandHandler CreateHandler()
        {
            return new CreateCalendarStatusCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                MapperMock.Object);
        }
    }
}