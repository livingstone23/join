using System.Linq.Expressions;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarCompanies;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.UpdateCalendarCompany;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarCompanies.Commands.UpdateCalendarCompany;

/// <summary>
/// Contains the unit tests for <see cref="UpdateCalendarCompanyCommandHandler"/>: tenant guard, validation branches,
/// the out-of-range user guard, and the reschedule-status branch.
/// </summary>
public sealed class UpdateCalendarCompanyCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant context is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new UpdateCalendarCompanyCommandTestContext();
        var handler = context.CreateHandler();

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");

        context.CalendarCompanyRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarCompany>()), Times.Never);
    }

    /// <summary>
    /// Verifies the not-found branch when the company has no calendar configuration yet.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyHasNoConfiguration_ShouldReturnNotConfiguredError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new UpdateCalendarCompanyCommandTestContext(companyId);
        var handler = context.CreateHandler();

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
        response.Errors.Should().Contain("The company has no calendar configuration. Create it first.");
    }

    /// <summary>
    /// Verifies the time-zone validation branch when the zone is unknown to the host.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTimeZoneIsUnknown_ShouldReturnInvalidTimeZoneError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new UpdateCalendarCompanyCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand() with { TimeZone = "Not/AZone" };
        var existing = SeedExistingEntity(companyId);
        context.SeedCalendarCompany(existing);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.InvalidTimeZone);
    }

    /// <summary>
    /// Verifies the out-of-range user guard when a user's own range falls outside the new company hours; the error
    /// code is <c>CALENDAR_USER_HOURS_OUT_OF_COMPANY_RANGE</c> and the message lists the offending users.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHoursFallOutsideNewCompanyHours_ShouldReturnOutOfRangeError()
    {
        var companyId = _fixture.Create<Guid>();
        var existing = SeedExistingEntity(companyId);
        var context = new UpdateCalendarCompanyCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand() with { BusinessHoursStart = new TimeOnly(10, 0), BusinessHoursEnd = new TimeOnly(16, 0) };
        var offendingUserId = Guid.NewGuid();
        context.SeedCalendarCompany(existing);
        context.SeedRescheduleStatus(companyId, existing.RescheduledByOthersStatusCompanyId);
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { new UserCompany { UserId = request.DefaultActivityUserId, CompanyId = companyId } });
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            new CalendarUserConfiguration
            {
                CompanyId = companyId,
                UserId = offendingUserId,
                BusinessHoursStart = new TimeOnly(8, 0),
                BusinessHoursEnd = new TimeOnly(17, 0)
            }
        });
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            new ApplicationUser { Id = offendingUserId, FirstName = "Alice", LastName = "Smith", Email = "alice@acme.test" }
        });

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.UserHoursOutOfCompanyRange);
        response.Errors.Should().Contain(s => s.Contains("Alice Smith") && s.Contains("alice@acme.test"));
        context.CalendarCompanyRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarCompany>()), Times.Never);
    }

    /// <summary>
    /// Verifies that narrowing the company hours to a window no user's hours fall inside produces the out-of-range
    /// error and lists every affected user in the response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMultipleUsersFallOutsideNewRange_ShouldListAllOfThem()
    {
        var companyId = _fixture.Create<Guid>();
        var existing = SeedExistingEntity(companyId);
        var context = new UpdateCalendarCompanyCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand() with { BusinessHoursStart = new TimeOnly(12, 0), BusinessHoursEnd = new TimeOnly(13, 0) };
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        context.SeedCalendarCompany(existing);
        context.SeedRescheduleStatus(companyId, existing.RescheduledByOthersStatusCompanyId);
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { new UserCompany { UserId = request.DefaultActivityUserId, CompanyId = companyId } });
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            new CalendarUserConfiguration { CompanyId = companyId, UserId = aliceId, BusinessHoursStart = new TimeOnly(8, 0), BusinessHoursEnd = new TimeOnly(17, 0) },
            new CalendarUserConfiguration { CompanyId = companyId, UserId = bobId, BusinessHoursStart = new TimeOnly(9, 0), BusinessHoursEnd = new TimeOnly(18, 0) }
        });
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            new ApplicationUser { Id = aliceId, FirstName = "Alice", LastName = "Smith", Email = "alice@acme.test" },
            new ApplicationUser { Id = bobId, FirstName = "Bob", LastName = "Jones", Email = "bob@acme.test" }
        });

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.UserHoursOutOfCompanyRange);
        response.Errors.Should().HaveCount(2);
        response.Errors.Should().Contain(s => s.Contains("Alice Smith"));
        response.Errors.Should().Contain(s => s.Contains("Bob Jones"));
    }

    /// <summary>
    /// Verifies the happy path: when no user's hours fall outside the new range, the entity is updated and the
    /// response carries the new values.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAllUsersFitInsideRange_ShouldUpdateAndReturnDto()
    {
        var companyId = _fixture.Create<Guid>();
        var existing = SeedExistingEntity(companyId);
        var context = new UpdateCalendarCompanyCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand();
        context.SeedCalendarCompany(existing);
        context.SeedRescheduleStatus(companyId, existing.RescheduledByOthersStatusCompanyId);
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { new UserCompany { UserId = request.DefaultActivityUserId, CompanyId = companyId } });
        context.UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarUserConfiguration>());
        // The response reports moduleEnabled (CalendarUserProvisioner.IsModuleActiveAsync).
        context.UnitOfWorkMock.SetupRepositoryRows(Array.Empty<JOIN.Domain.Admin.SystemModule>());

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.ModuleEnabled.Should().BeFalse();
        response.Data.Id.Should().Be(existing.Id);
        response.Data.TimeZone.Should().Be(request.TimeZone);
        context.CalendarCompanyRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarCompany>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that when the reschedule status is requested but does not resolve to a company row, the handler
    /// returns <c>CALENDAR_STATUS_NOT_ENABLED</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRescheduleStatusIsUnknown_ShouldReturnStatusNotEnabledError()
    {
        var companyId = _fixture.Create<Guid>();
        var existing = SeedExistingEntity(companyId);
        var context = new UpdateCalendarCompanyCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand() with { RescheduledByOthersStatusCompanyId = Guid.NewGuid() };
        context.SeedCalendarCompany(existing);
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { new UserCompany { UserId = request.DefaultActivityUserId, CompanyId = companyId } });
        context.UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarUserConfiguration>());
        context.CalendarStatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(request.RescheduledByOthersStatusCompanyId!.Value))
            .ReturnsAsync((CalendarStatusCompany?)null);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.StatusNotEnabled);
    }

    /// <summary>
    /// Creates a valid update command for reuse across test scenarios.
    /// </summary>
    private UpdateCalendarCompanyCommand CreateValidCommand()
    {
        return new UpdateCalendarCompanyCommand
        {
            DefaultActivityUserId = _fixture.Create<Guid>(),
            TimeZone = "America/Managua",
            IsOpen24Hours = false,
            BusinessHoursStart = new TimeOnly(8, 0),
            BusinessHoursEnd = new TimeOnly(17, 0),
            DefaultCalendarName = "Mi calendario",
            RescheduledByOthersStatusCompanyId = null
        };
    }

    /// <summary>
    /// Builds an existing configuration with the supplied company id; the rest of the columns are fixed so each test
    /// can override what it cares about through the request.
    /// </summary>
    private static CalendarCompany SeedExistingEntity(Guid companyId)
    {
        var company = new CalendarCompany
        {
            CompanyId = companyId,
            DefaultActivityUserId = Guid.NewGuid(),
            TimeZone = "America/Managua",
            IsOpen24Hours = false,
            BusinessHoursStart = new TimeOnly(8, 0),
            BusinessHoursEnd = new TimeOnly(17, 0),
            DefaultCalendarName = "Mi calendario",
            RescheduledByOthersStatusCompanyId = Guid.NewGuid()
        };
        EntityIdSetter.SetId(company, Guid.NewGuid());
        return company;
    }

    /// <summary>
    /// Holds the reusable mocks for the update handler tests. CalendarCompanySettingsValidator is instantiated
    /// directly so its checks are exercised through the unit-of-work mocks.
    /// </summary>
    private sealed class UpdateCalendarCompanyCommandTestContext
    {
        public UpdateCalendarCompanyCommandTestContext() : this(Guid.Empty) { }

        public UpdateCalendarCompanyCommandTestContext(Guid companyId)
        {
            CompanyId = companyId;
            if (companyId != Guid.Empty)
            {
                CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            }

            // CalendarCompany uses a tracked list so the handler's GetAllIncludingDeletedAsync and GetAsync lookups
            // resolve the row the test seeded. SetupRepositoryRows would create a separate mock that does not
            // share state with the GetAsync/UpdateAsync setup here.
            CalendarCompanyRepositoryMock
                .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<Expression<Func<CalendarCompany, bool>>>()))
                .ReturnsAsync((Expression<Func<CalendarCompany, bool>> predicate) => (IEnumerable<CalendarCompany>)CalendarCompanyRows.Where(predicate.Compile()).ToList());
            CalendarCompanyRepositoryMock
                .Setup(x => x.GetAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => CalendarCompanyRows.FirstOrDefault(c => c.Id == id));
            CalendarCompanyRepositoryMock
                .Setup(x => x.UpdateAsync(It.IsAny<CalendarCompany>()))
                .ReturnsAsync(true);

            // CalendarStatusCompany uses a tracked list so the catalog reader's GetAllIncludingDeletedAsync and
            // GetAsync lookups resolve the rows the test seeded.
            CalendarStatusCompanyRepositoryMock
                .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<Expression<Func<CalendarStatusCompany, bool>>>()))
                .ReturnsAsync((Expression<Func<CalendarStatusCompany, bool>> predicate) => (IEnumerable<CalendarStatusCompany>)CalendarStatusCompanies.Where(predicate.Compile()).ToList());
            CalendarStatusCompanyRepositoryMock
                .Setup(x => x.GetAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => CalendarStatusCompanies.FirstOrDefault(c => c.Id == id));

            // CalendarStatus (global) uses a tracked list for the catalog reader's RESCHEDULED lookup.
            GlobalCalendarStatusRepositoryMock
                .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<Expression<Func<CalendarStatus, bool>>>()))
                .ReturnsAsync((Expression<Func<CalendarStatus, bool>> predicate) => (IEnumerable<CalendarStatus>)GlobalCalendarStatuses.Where(predicate.Compile()).ToList());
            GlobalCalendarStatusRepositoryMock
                .Setup(x => x.GetAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => GlobalCalendarStatuses.FirstOrDefault(c => c.Id == id));

            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarCompany>()).Returns(CalendarCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatusCompany>()).Returns(CalendarStatusCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatus>()).Returns(GlobalCalendarStatusRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<UserCompany>());
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarUserConfiguration>());
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<ApplicationUser>());
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarEvent>());
        }

        /// <summary>
        /// Adds an existing <see cref="CalendarCompany"/> to the tracked list so the handler finds it through
        /// both <c>GetAllIncludingDeletedAsync</c> and <c>GetAsync</c>.
        /// </summary>
        public void SeedCalendarCompany(CalendarCompany row)
        {
            CalendarCompanyRows.Add(row);
        }

        /// <summary>
        /// Seeds the company and global status repositories together so the catalog reader resolves the company's
        /// <c>RESCHEDULED</c> status during the validation steps.
        /// </summary>
        public void SeedRescheduleStatus(Guid companyId, Guid companyStatusId)
        {
            var globalRescheduleId = Guid.NewGuid();
            var globalStatus = new CalendarStatus { Code = CalendarCodes.Statuses.Rescheduled, IsActive = true };
            EntityIdSetter.SetId(globalStatus, globalRescheduleId);
            GlobalCalendarStatuses.Add(globalStatus);
            var companyStatus = new CalendarStatusCompany
            {
                CompanyId = companyId,
                CalendarStatusId = globalRescheduleId,
                Name = "Rescheduled",
                IsActive = true
            };
            EntityIdSetter.SetId(companyStatus, companyStatusId);
            CalendarStatusCompanies.Add(companyStatus);
        }

        public Guid CompanyId { get; }
        public List<CalendarCompany> CalendarCompanyRows { get; } = new();
        public List<CalendarStatusCompany> CalendarStatusCompanies { get; } = new();
        public List<CalendarStatus> GlobalCalendarStatuses { get; } = new();
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarCompany>> CalendarCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatusCompany>> CalendarStatusCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatus>> GlobalCalendarStatusRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public UpdateCalendarCompanyCommandHandler CreateHandler()
        {
            var provisioner = new CalendarUserProvisioner(UnitOfWorkMock.Object);
            var catalogReader = new CalendarCatalogReader(UnitOfWorkMock.Object);
            var settingsValidator = new CalendarCompanySettingsValidator(UnitOfWorkMock.Object, catalogReader);
            return new UpdateCalendarCompanyCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                settingsValidator,
                provisioner);
        }
    }
}