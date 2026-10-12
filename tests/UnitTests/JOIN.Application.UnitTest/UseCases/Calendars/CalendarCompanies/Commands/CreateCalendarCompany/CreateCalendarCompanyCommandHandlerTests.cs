using System.Linq.Expressions;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarCompanies;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.CreateCalendarCompany;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarCompanies.Commands.CreateCalendarCompany;

/// <summary>
/// Contains the unit tests for <see cref="CreateCalendarCompanyCommandHandler"/>: tenant guard, the reschedule-status
/// default, validation errors, 24-hour mode, and the provisioning of pending users through
/// <see cref="CalendarUserProvisioner"/>.
/// </summary>
public sealed class CreateCalendarCompanyCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    private static SystemModule BuildSystemModuleWithId(Guid id)
    {
        var module = new SystemModule
        {
            Name = CalendarUserProvisioner.CalendarModuleName,
            IsActive = true
        };
        EntityIdSetter.SetId(module, id);
        return module;
    }

    /// <summary>
    /// Verifies the early exit when the tenant context is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new CreateCalendarCompanyCommandTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");

        context.CalendarCompanyRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarCompany>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the time zone validation branch when the zone is not an IANA id known to the host.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTimeZoneIsUnknown_ShouldReturnInvalidTimeZoneError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new CreateCalendarCompanyCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand() with { TimeZone = "Mars/Olympus_Mons" };

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.InvalidTimeZone);
        response.Errors.Should().Contain(s => s.Contains("Mars/Olympus_Mons") && s.Contains("not a valid IANA time zone"));
    }

    /// <summary>
    /// Verifies the duplicate guard when the company already has a calendar configuration.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyAlreadyConfigured_ShouldReturnDuplicateError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new CreateCalendarCompanyCommandTestContext(companyId);
        var handler = context.CreateHandler();

        context.SeedCalendarCompany(new CalendarCompany { CompanyId = companyId, TimeZone = "America/Managua" });

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyAlreadyConfigured);
        response.Errors.Should().Contain("The company already has a calendar configuration.");

        context.CalendarCompanyRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarCompany>()), Times.Never);
    }

    /// <summary>
    /// Verifies the business-hours validation branch when the end is not later than the start.
    /// </summary>
    [Fact]
    public async Task Handle_WhenBusinessHoursEndBeforeStart_ShouldReturnInvalidBusinessHoursError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new CreateCalendarCompanyCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand() with
        {
            BusinessHoursStart = new TimeOnly(17, 0),
            BusinessHoursEnd = new TimeOnly(9, 0)
        };

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.InvalidBusinessHours);
    }

    /// <summary>
    /// Verifies the default-user guard when the user is not an active member of the company.
    /// </summary>
    [Fact]
    public async Task Handle_WhenDefaultActivityUserIsNotMember_ShouldReturnDefaultUserNotInCompanyError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new CreateCalendarCompanyCommandTestContext(companyId);
        var handler = context.CreateHandler();

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.DefaultUserNotInCompany);
    }

    /// <summary>
    /// Verifies the reschedule-status branch when the requested status id does not resolve to a company row.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRescheduleStatusIsUnknown_ShouldReturnStatusNotEnabledError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new CreateCalendarCompanyCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand() with { RescheduledByOthersStatusCompanyId = Guid.NewGuid() };
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { new UserCompany { UserId = request.DefaultActivityUserId, CompanyId = companyId } });
        context.CalendarStatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(request.RescheduledByOthersStatusCompanyId!.Value))
            .ReturnsAsync((CalendarStatusCompany?)null);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.StatusNotEnabled);
    }

    /// <summary>
    /// Verifies that when <c>RescheduledByOthersStatusCompanyId</c> is omitted the resolver falls back to the
    /// company's <c>RESCHEDULED</c> status, so the response carries that name.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRescheduleStatusOmitted_ShouldUseCompaniesRescheduledStatus()
    {
        var companyId = _fixture.Create<Guid>();
        var rescheduleStatusId = Guid.NewGuid();
        var context = new CreateCalendarCompanyCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand() with { RescheduledByOthersStatusCompanyId = null };
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { new UserCompany { UserId = request.DefaultActivityUserId, CompanyId = companyId } });
        context.SeedRescheduleStatus(rescheduleStatusId);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.RescheduledByOthersStatusCompanyId.Should().Be(rescheduleStatusId);
        response.Data.RescheduledByOthersStatusName.Should().Be("Rescheduled");
    }

    /// <summary>
    /// Verifies that an <c>IsOpen24Hours</c> request ignores the business-hours payload on the saved entity.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIsOpen24Hours_ShouldIgnoreBusinessHoursOnEntity()
    {
        var companyId = _fixture.Create<Guid>();
        var rescheduleStatusId = Guid.NewGuid();
        var context = new CreateCalendarCompanyCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand() with
        {
            IsOpen24Hours = true,
            BusinessHoursStart = new TimeOnly(8, 0),
            BusinessHoursEnd = new TimeOnly(17, 0),
            RescheduledByOthersStatusCompanyId = null
        };
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { new UserCompany { UserId = request.DefaultActivityUserId, CompanyId = companyId } });
        context.SeedRescheduleStatus(rescheduleStatusId);

        CalendarCompany? inserted = null;
        context.CalendarCompanyRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarCompany>()))
            .Callback<CalendarCompany>(e => inserted = e)
            .ReturnsAsync(true);

        var result = await handler.Handle(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        inserted.Should().NotBeNull();
        inserted!.IsOpen24Hours.Should().BeTrue();
        inserted.BusinessHoursStart.Should().BeNull();
        inserted.BusinessHoursEnd.Should().BeNull();
    }

    /// <summary>
    /// Verifies that the handler invokes <see cref="CalendarUserProvisioner.EnsureUserCalendarAsync"/> once per
    /// pending active member of the company (those without a row in <c>Calendar.UserConfigurations</c>), and reports
    /// the number of users it provisioned.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyHasPendingUsers_ShouldProvisionEveryActiveMember()
    {
        var companyId = _fixture.Create<Guid>();
        var defaultActivityUserId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var otherMemberId = Guid.NewGuid();
        var alreadyProvisionedId = Guid.NewGuid();
        var rescheduleStatusId = Guid.NewGuid();
        var calendarModuleId = Guid.NewGuid();
        var context = new CreateCalendarCompanyCommandTestContext(companyId);
        var request = CreateValidCommand() with
        {
            DefaultActivityUserId = defaultActivityUserId,
            RescheduledByOthersStatusCompanyId = null
        };
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            new UserCompany { UserId = defaultActivityUserId, CompanyId = companyId },
            new UserCompany { UserId = memberId, CompanyId = companyId },
            new UserCompany { UserId = otherMemberId, CompanyId = companyId },
            new UserCompany { UserId = alreadyProvisionedId, CompanyId = companyId }
        });
        // Capture the inner mock for CalendarUserConfiguration so InsertAsync (and the verification) operate
        // on the same instance the handler reaches through GetRepository<CalendarUserConfiguration>().
        var configurationRepositoryMock = context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            new CalendarUserConfiguration { UserId = alreadyProvisionedId, CompanyId = companyId }
        });
        configurationRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarUserConfiguration>()))
            .ReturnsAsync(true);
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            new ApplicationUser { Id = memberId, FirstName = "Alice", LastName = "Smith", Email = "alice@acme.test", IsActive = true },
            new ApplicationUser { Id = otherMemberId, FirstName = "Bob", LastName = "Jones", Email = "bob@acme.test", IsActive = true }
        });
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            BuildSystemModuleWithId(calendarModuleId)
        });
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            new CompanyModule { CompanyId = companyId, ModuleId = calendarModuleId, IsActive = true }
        });
        context.SeedRescheduleStatus(rescheduleStatusId);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Contain("Users provisioned: 2.");
        response.Data!.ModuleEnabled.Should().BeTrue();
        context.UserCalendarRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<UserCalendar>()), Times.Exactly(2));
        configurationRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarUserConfiguration>()), Times.Exactly(2));
    }

    /// <summary>
    /// Creates a valid create command for reuse across test scenarios.
    /// </summary>
    private CreateCalendarCompanyCommand CreateValidCommand()
    {
        return new CreateCalendarCompanyCommand
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
    /// Holds the reusable mocks for the create handler tests. CalendarCompanySettingsValidator,
    /// CalendarCompanyProvisioning and CalendarUserProvisioner are instantiated directly because they are concrete
    /// classes built around <see cref="IUnitOfWork"/>; the catalogue reader uses the unit-of-work mocks so it
    /// reads from the same controlled data set.
    /// </summary>
    private sealed class CreateCalendarCompanyCommandTestContext
    {
        public CreateCalendarCompanyCommandTestContext(Guid companyId)
        {
            CompanyId = companyId;
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);

            // CalendarCompany uses a tracked list so the inserted row becomes visible to subsequent queries —
            // the IUnitOfWork mock does not simulate the EF change tracker. Tests pre-seed by calling SeedCalendarCompany.
            CalendarCompanyRepositoryMock
                .Setup(x => x.InsertAsync(It.IsAny<CalendarCompany>()))
                .Callback<CalendarCompany>(e => CalendarCompanyRows.Add(e))
                .ReturnsAsync(true);
            CalendarCompanyRepositoryMock
                .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<Expression<Func<CalendarCompany, bool>>>()))
                .ReturnsAsync((Expression<Func<CalendarCompany, bool>> predicate) => (IEnumerable<CalendarCompany>)CalendarCompanyRows.Where(predicate.Compile()).ToList());

            // CalendarStatusCompany uses a tracked list so the catalogue reader's GetAllIncludingDeletedAsync and
            // GetAsync lookups resolve the rows the test seeded.
            CalendarStatusCompanyRepositoryMock
                .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<Expression<Func<CalendarStatusCompany, bool>>>()))
                .ReturnsAsync((Expression<Func<CalendarStatusCompany, bool>> predicate) => (IEnumerable<CalendarStatusCompany>)CalendarStatusCompanies.Where(predicate.Compile()).ToList());
            CalendarStatusCompanyRepositoryMock
                .Setup(x => x.GetAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => CalendarStatusCompanies.FirstOrDefault(c => c.Id == id));

            // CalendarStatus (global) uses a tracked list for the catalogue reader's RESCHEDULED lookup.
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

            // Empty defaults for the other tables; tests that need rows set them up via SetupRepositoryRows.
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<UserCompany>());
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarUserConfiguration>());
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<ApplicationUser>());
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<SystemModule>());
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CompanyModule>());

            UserCalendarRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<UserCalendar>())).ReturnsAsync(true);
            CalendarUserConfigurationRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<CalendarUserConfiguration>())).ReturnsAsync(true);
        }

        /// <summary>
        /// Adds a <see cref="CalendarCompany"/> to the tracked list so the duplicate guard can be exercised.
        /// </summary>
        public void SeedCalendarCompany(CalendarCompany row)
        {
            CalendarCompanyRows.Add(row);
        }

        /// <summary>
        /// Seeds the company and global status repositories together so the catalogue reader resolves the company's
        /// <c>RESCHEDULED</c> status during the validation steps.
        /// </summary>
        public void SeedRescheduleStatus(Guid companyStatusId)
        {
            var globalRescheduleId = Guid.NewGuid();
            var globalStatus = new CalendarStatus { Code = CalendarCodes.Statuses.Rescheduled, IsActive = true };
            EntityIdSetter.SetId(globalStatus, globalRescheduleId);
            GlobalCalendarStatuses.Add(globalStatus);
            var companyStatus = new CalendarStatusCompany
            {
                CompanyId = CompanyId,
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
        public Mock<IGenericRepository<UserCalendar>> UserCalendarRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarUserConfiguration>> CalendarUserConfigurationRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public CreateCalendarCompanyCommandHandler CreateHandler()
        {
            // SetupRepositoryRows binds GetRepository<T>() to an INNER mock that the test already seeded
            // with the right rows. Creating the handler would otherwise introduce no extra wiring, but the
            // original implementation re-bound GetRepository<CalendarUserConfiguration>() to the field
            // CalendarUserConfigurationRepositoryMock (which never had its GetAllIncludingDeletedAsync set
            // up), so every query for already-provisioned users returned null and provisioning crashed.
            UnitOfWorkMock.Setup(x => x.GetRepository<UserCalendar>()).Returns(UserCalendarRepositoryMock.Object);
            // CalendarUserConfiguration remains bound to the mock created by SetupRepositoryRows above.

            var provisioner = new CalendarUserProvisioner(UnitOfWorkMock.Object);
            var catalogReader = new CalendarCatalogReader(UnitOfWorkMock.Object);
            var settingsValidator = new CalendarCompanySettingsValidator(UnitOfWorkMock.Object, catalogReader);
            var provisioning = new CalendarCompanyProvisioning(UnitOfWorkMock.Object, provisioner);
            return new CreateCalendarCompanyCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                settingsValidator,
                provisioning,
                provisioner);
        }
    }
}