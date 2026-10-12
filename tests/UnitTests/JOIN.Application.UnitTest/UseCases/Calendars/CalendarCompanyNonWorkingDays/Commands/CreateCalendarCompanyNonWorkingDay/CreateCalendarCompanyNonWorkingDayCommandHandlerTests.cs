using AutoFixture;
using FluentAssertions;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.CreateCalendarCompanyNonWorkingDay;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.CreateCalendarCompanyNonWorkingDay;

/// <summary>
/// Contains the unit tests for <see cref="CreateCalendarCompanyNonWorkingDayCommandHandler"/>: tenant guard, the
/// duplicate-date guard, and the happy path.
/// </summary>
public sealed class CreateCalendarCompanyNonWorkingDayCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant context is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new CreateCalendarCompanyNonWorkingDayCommandTestContext();
        var handler = context.CreateHandler();

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");
        context.HolidayRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarCompanyNonWorkingDay>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate guard when the date already has a holiday in the company.
    /// </summary>
    [Fact]
    public async Task Handle_WhenHolidayAlreadyExists_ShouldReturnDuplicateError()
    {
        var companyId = _fixture.Create<Guid>();
        var date = new DateOnly(2026, 9, 15);
        var context = new CreateCalendarCompanyNonWorkingDayCommandTestContext(companyId);
        var handler = context.CreateHandler();
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            new CalendarCompanyNonWorkingDay { CompanyId = companyId, Date = date, Name = "Already exists" }
        });

        var response = await handler.Handle(CreateValidCommand() with { Date = date }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.NonWorkingDayDuplicate);
        response.Errors.Should().Contain("The company already has a holiday on this date.");
        context.HolidayRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarCompanyNonWorkingDay>()), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: the entity is inserted, the mapper is used, and the DTO carries the new fields.
    /// </summary>
    [Fact]
    public async Task Handle_WhenHolidayIsNew_ShouldCreateAndReturnDto()
    {
        var companyId = _fixture.Create<Guid>();
        var date = new DateOnly(2026, 9, 15);
        var context = new CreateCalendarCompanyNonWorkingDayCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand() with { Date = date, Name = "  Independence Day  ", IsRecurringYearly = true };
        context.UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarCompanyNonWorkingDay>());
        CalendarCompanyNonWorkingDay? inserted = null;
        context.HolidayRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarCompanyNonWorkingDay>()))
            .Callback<CalendarCompanyNonWorkingDay>(e => inserted = e)
            .ReturnsAsync(true);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Holiday created successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Name.Should().Be("Independence Day");
        response.Data.IsRecurringYearly.Should().BeTrue();
        inserted.Should().NotBeNull();
        inserted!.CompanyId.Should().Be(companyId);
        inserted.Date.Should().Be(date);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid create command for reuse across test scenarios.
    /// </summary>
    private static CreateCalendarCompanyNonWorkingDayCommand CreateValidCommand()
    {
        return new CreateCalendarCompanyNonWorkingDayCommand
        {
            Date = new DateOnly(2026, 9, 15),
            Name = "Independence Day",
            IsRecurringYearly = false
        };
    }

    /// <summary>
    /// Holds the reusable mocks for the create handler tests.
    /// </summary>
    private sealed class CreateCalendarCompanyNonWorkingDayCommandTestContext
    {
        public CreateCalendarCompanyNonWorkingDayCommandTestContext() : this(Guid.Empty) { }

        public CreateCalendarCompanyNonWorkingDayCommandTestContext(Guid companyId)
        {
            CompanyId = companyId;
            if (companyId != Guid.Empty)
            {
                CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            }

            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarCompanyNonWorkingDay>()).Returns(HolidayRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarCompanyNonWorkingDay>());
        }

        public Guid CompanyId { get; }
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarCompanyNonWorkingDay>> HolidayRepositoryMock { get; } = new();
        public Mock<ICalendarMapper> MapperMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public CreateCalendarCompanyNonWorkingDayCommandHandler CreateHandler()
    {
        // The handler's DTO mapper is the production ICalendarMapper; the handler calls ToDto on the inserted entity.
        // Without this setup Moq returns null and response.Data is null.
        MapperMock
            .Setup(x => x.ToDto(It.IsAny<JOIN.Domain.Calendars.CalendarCompanyNonWorkingDay>()))
            .Returns((JOIN.Domain.Calendars.CalendarCompanyNonWorkingDay e) => new CalendarCompanyNonWorkingDayDto
            {
                Id = e.Id,
                Date = e.Date,
                Name = e.Name,
                IsRecurringYearly = e.IsRecurringYearly,
                OccursOn = e.Date,
                CreatedAt = e.Created
            });
        return new CreateCalendarCompanyNonWorkingDayCommandHandler(
            UnitOfWorkMock.Object,
            CurrentUserServiceMock.Object,
            MapperMock.Object);
    }
    }
}