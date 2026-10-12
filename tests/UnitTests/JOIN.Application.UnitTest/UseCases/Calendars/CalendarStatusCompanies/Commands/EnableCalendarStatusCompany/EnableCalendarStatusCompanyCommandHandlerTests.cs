using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.EnableCalendarStatusCompany;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarStatusCompanies.Commands.EnableCalendarStatusCompany;

/// <summary>
/// Contains the unit tests for enabling a global calendar status in the current company
/// (SPEC 44 F10 "Catálogos"). The handler copies the global values and rejects duplicates.
/// </summary>
public sealed class EnableCalendarStatusCompanyCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    private static CalendarStatus BuildGlobalStatus(
        Guid id,
        string code = "ON_HOLD",
        string name = "On hold",
        string color = "#FFB300",
        bool blocksTime = true,
        bool isFinal = false,
        bool isActive = true,
        int sortOrder = 50)
    {
        var status = new CalendarStatus
        {
            Code = code,
            Name = name,
            Color = color,
            BlocksTime = blocksTime,
            IsFinal = isFinal,
            IsActive = isActive,
            SortOrder = sortOrder,
            GcRecord = 0
        };
        EntityIdSetter.SetId(status, id);
        return status;
    }

    /// <summary>
    /// Verifies the duplicate-guard rejects an attempt to enable the same status twice in the company.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAlreadyEnabled_ShouldReturnStatusAlreadyEnabledError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var globalId = _fixture.Create<Guid>();
        var context = new EnableCalendarStatusCompanyCommandTestContext(companyId);
        var request = new EnableCalendarStatusCompanyCommand
        {
            CalendarStatusId = globalId
        };

        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatus>())
            .Returns(context.StatusRepositoryMock.Object);
        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(globalId))
            .ReturnsAsync(BuildGlobalStatus(globalId, code: string.Empty, name: string.Empty, color: string.Empty, blocksTime: false, isFinal: false, sortOrder: 0));

        context.UnitOfWorkMock.SetupRepositoryRows(
        [
            new CalendarStatusCompany { CalendarStatusId = globalId, CompanyId = companyId, GcRecord = 0 }
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_STATUS_ALREADY_ENABLED");

        context.StatusCompanyRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarStatusCompany>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: the global values are copied into the company row.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCopyGlobalValuesAndInsertCompanyRow()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var globalId = _fixture.Create<Guid>();
        var context = new EnableCalendarStatusCompanyCommandTestContext(companyId);
        var request = new EnableCalendarStatusCompanyCommand
        {
            CalendarStatusId = globalId
        };
        CalendarStatusCompany? inserted = null;

        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatus>())
            .Returns(context.StatusRepositoryMock.Object);
        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(globalId))
            .ReturnsAsync(BuildGlobalStatus(globalId));

        context.StatusCompanyRepositoryMock.SetupRows(Array.Empty<CalendarStatusCompany>());

        context.StatusCompanyRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarStatusCompany>()))
            .Callback<CalendarStatusCompany>(entity => inserted = entity)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Calendar status enabled successfully.");
        response.Data.Should().NotBeNull();
        inserted.Should().NotBeNull();
        inserted!.CompanyId.Should().Be(companyId);
        inserted.CalendarStatusId.Should().Be(globalId);
        inserted.Name.Should().Be("On hold");
        inserted.Color.Should().Be("#FFB300");
        inserted.IsActive.Should().BeTrue();
        inserted.SortOrder.Should().Be(50);

        context.StatusCompanyRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarStatusCompany>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the enable handler tests.
    /// </summary>
    private sealed class EnableCalendarStatusCompanyCommandTestContext
    {
        public EnableCalendarStatusCompanyCommandTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole(It.IsAny<string>())).Returns(false);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatusCompany>())
                .Returns(StatusCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatus>> StatusRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatusCompany>> StatusCompanyRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public EnableCalendarStatusCompanyCommandHandler CreateHandler()
        {
            return new EnableCalendarStatusCompanyCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}