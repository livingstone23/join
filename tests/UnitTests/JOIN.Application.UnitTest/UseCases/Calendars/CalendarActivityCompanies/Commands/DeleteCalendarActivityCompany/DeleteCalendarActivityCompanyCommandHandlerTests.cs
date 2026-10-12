using JOIN.Application.UnitTest.Common.TestDoubles;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.DeleteCalendarActivityCompany;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarActivityCompanies.Commands.DeleteCalendarActivityCompany;

/// <summary>
/// Contains the unit tests for removing a calendar activity type from a company (SPEC 44 F10 "Catálogos").
/// In-use types are deactivated instead of deleted (<c>Data = false</c>).
/// </summary>
public sealed class DeleteCalendarActivityCompanyCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the in-use branch: the company row is deactivated and the response reports the block.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActivityHasEvents_ShouldDeactivateAndReturnActivityInUse()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteCalendarActivityCompanyCommandTestContext(companyId);
        var commandId = _fixture.Create<Guid>();
        var entity = new CalendarActivityCompany
        {
            CompanyId = companyId,
            CalendarActivityId = _fixture.Create<Guid>(),
            IsActive = true,
            GcRecord = 0
        };
        EntityIdSetter.SetId(entity, commandId);
        CalendarActivityCompany? updated = null;

        context.ActivityCompanyRepositoryMock
            .Setup(x => x.GetAsync(commandId))
            .ReturnsAsync(entity);
        context.CalendarEventsMock
            .Setup(x => x.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CalendarEvent, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.ActivityCompanyRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<CalendarActivityCompany>()))
            .Callback<CalendarActivityCompany>(row => updated = row)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteCalendarActivityCompanyCommand(commandId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("The activity type has activities; it was deactivated instead of deleted.");
        response.Data.Should().BeFalse();
        updated.Should().NotBeNull();
        updated!.IsActive.Should().BeFalse();

        context.ActivityCompanyRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarActivityCompany>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies the happy path: with no events pointing at the row, the handler logically deletes it.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActivityHasNoEvents_ShouldMarkAsDeletedAndReturnSuccess()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteCalendarActivityCompanyCommandTestContext(companyId);
        var commandId = _fixture.Create<Guid>();
        var entity = new CalendarActivityCompany
        {
            CompanyId = companyId,
            CalendarActivityId = _fixture.Create<Guid>(),
            IsActive = true,
            GcRecord = 0
        };
        EntityIdSetter.SetId(entity, commandId);
        CalendarActivityCompany? updated = null;

        context.ActivityCompanyRepositoryMock
            .Setup(x => x.GetAsync(commandId))
            .ReturnsAsync(entity);
        context.CalendarEventsMock
            .Setup(x => x.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CalendarEvent, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        context.ActivityCompanyRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<CalendarActivityCompany>()))
            .Callback<CalendarActivityCompany>(row => updated = row)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteCalendarActivityCompanyCommand(commandId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Calendar activity type removed from the company.");
        response.Data.Should().BeTrue();
        updated.Should().NotBeNull();
        updated!.GcRecord.Should().NotBe(0);
        updated.IsActive.Should().BeTrue();

        context.ActivityCompanyRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarActivityCompany>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the delete handler tests.
    /// </summary>
    private sealed class DeleteCalendarActivityCompanyCommandTestContext
    {
        public DeleteCalendarActivityCompanyCommandTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole(It.IsAny<string>())).Returns(false);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivityCompany>())
                .Returns(ActivityCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.CalendarEvents).Returns(CalendarEventsMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarActivityCompany>> ActivityCompanyRepositoryMock { get; } = new();
        public Mock<ICalendarEventRepository> CalendarEventsMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public DeleteCalendarActivityCompanyCommandHandler CreateHandler()
        {
            return new DeleteCalendarActivityCompanyCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}