using JOIN.Application.UnitTest.Common.TestDoubles;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.UpdateCalendarStatusCompany;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarStatusCompanies.Commands.UpdateCalendarStatusCompany;

/// <summary>
/// Contains the unit tests for updating a calendar status of a company (SPEC 44 F10 "Catálogos").
/// Deactivating a status that is the initial status of an activity type is rejected.
/// </summary>
public sealed class UpdateCalendarStatusCompanyCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    private static CalendarStatus BuildCalendarStatus(Guid id, string code)
    {
        var status = new CalendarStatus { Code = code, GcRecord = 0, IsActive = true };
        EntityIdSetter.SetId(status, id);
        return status;
    }

    /// <summary>
    /// Verifies the validation branch: deactivating a status that is the initial status of an
    /// activity type is rejected (<c>CALENDAR_STATUS_IN_USE_AS_INITIAL</c>).
    /// </summary>
    [Fact]
    public async Task Handle_WhenDeactivatingInitialStatus_ShouldReturnStatusInUseAsInitialError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var globalId = _fixture.Create<Guid>();
        var companyStatusId = _fixture.Create<Guid>();
        var context = new UpdateCalendarStatusCompanyCommandTestContext(companyId);
        var request = new UpdateCalendarStatusCompanyCommand
        {
            Name = "Pending",
            Color = "#FFB300",
            IsActive = false,
            SortOrder = 10
        };
        EntityIdSetter.SetId(request, companyStatusId);

        var entity = new CalendarStatusCompany
        {
            CompanyId = companyId,
            CalendarStatusId = globalId,
            IsActive = true,
            GcRecord = 0
        };
        EntityIdSetter.SetId(entity, companyStatusId);

        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatusCompany>())
            .Returns(context.StatusCompanyRepositoryMock.Object);
        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatus>())
            .Returns(context.StatusRepositoryMock.Object);

        context.StatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(companyStatusId))
            .ReturnsAsync(entity);
        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(globalId))
            .ReturnsAsync(BuildCalendarStatus(globalId, "ON_HOLD"));

        // Another activity type uses this status as its initial status.
        context.UnitOfWorkMock.SetupRepositoryRows(
        [
            new CalendarActivityCompany
            {
                CompanyId = companyId,
                InitialCalendarStatusCompanyId = companyStatusId,
                GcRecord = 0
            }
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_STATUS_IN_USE_AS_INITIAL");

        context.StatusCompanyRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarStatusCompany>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: an existing status is mutated and persisted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldPersistChangesAndReturnDto()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var globalId = _fixture.Create<Guid>();
        var companyStatusId = _fixture.Create<Guid>();
        var context = new UpdateCalendarStatusCompanyCommandTestContext(companyId);
        var request = new UpdateCalendarStatusCompanyCommand
        {
            Name = "  Updated status  ",
            Color = "  #FF7043 ",
            IsActive = true,
            SortOrder = 25
        };
        EntityIdSetter.SetId(request, companyStatusId);

        var entity = new CalendarStatusCompany
        {
            CompanyId = companyId,
            CalendarStatusId = globalId,
            IsActive = true,
            GcRecord = 0,
            Name = "Old name"
        };
        EntityIdSetter.SetId(entity, companyStatusId);
        CalendarStatusCompany? updated = null;

        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatusCompany>())
            .Returns(context.StatusCompanyRepositoryMock.Object);
        context.UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatus>())
            .Returns(context.StatusRepositoryMock.Object);

        context.StatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(companyStatusId))
            .ReturnsAsync(entity);
        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(globalId))
            .ReturnsAsync(BuildCalendarStatus(globalId, "ON_HOLD"));

        // No activity uses this status as initial.
        context.UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarActivityCompany>());

        context.StatusCompanyRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<CalendarStatusCompany>()))
            .Callback<CalendarStatusCompany>(row => updated = row)
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
        updated.IsActive.Should().BeTrue();
        updated.SortOrder.Should().Be(25);

        context.StatusCompanyRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarStatusCompany>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the update handler tests.
    /// </summary>
    private sealed class UpdateCalendarStatusCompanyCommandTestContext
    {
        public UpdateCalendarStatusCompanyCommandTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole(It.IsAny<string>())).Returns(false);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatusCompany>> StatusCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatus>> StatusRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public UpdateCalendarStatusCompanyCommandHandler CreateHandler()
        {
            return new UpdateCalendarStatusCompanyCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                new CalendarStatusCompanyRemovalGuard(UnitOfWorkMock.Object));
        }
    }
}