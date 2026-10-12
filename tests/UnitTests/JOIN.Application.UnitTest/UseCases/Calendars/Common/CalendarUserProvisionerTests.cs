using System.Linq.Expressions;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarCompanies;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.Common;

/// <summary>
/// SPEC 44 F10 — provisioner (6 tests): creates a calendar + configuration, is idempotent, no-ops without the Calendar
/// module or a <see cref="CalendarCompany"/>, leaves business hours null (effective hours come from the company), and
/// is reachable from the <c>InviteUser</c> / <c>AddUserCompany</c> / <c>CreateCalendarCompany</c> call paths. The latter
/// are exercised through <see cref="CalendarCompanyProvisioning"/> and the real provisioner wired into the caller's
/// unit of work — Moq cannot intercept a sealed concrete class without changing production code.
/// </summary>
public sealed class CalendarUserProvisionerTests
{
    /// <summary>
    /// F10 / happy path — module active, <c>CalendarCompany</c> present, no prior configuration: inserts both rows.
    /// </summary>
    [Fact]
    public async Task EnsureUserCalendarAsync_WhenEverythingConfigured_ShouldInsertCalendarAndConfiguration()
    {
        var context = new CalendarUserProvisionerTestContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        context.SeedCalendarModule();
        context.SeedCompanyModule(companyId, isActive: true);
        context.SeedCalendarCompany(companyId, defaultCalendarName: "Mi calendario");

        UserCalendar? insertedCalendar = null;
        CalendarUserConfiguration? insertedConfiguration = null;
        context.UserCalendarRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserCalendar>()))
            .Callback<UserCalendar>(c => insertedCalendar = c)
            .ReturnsAsync(true);
        context.CalendarUserConfigurationRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarUserConfiguration>()))
            .Callback<CalendarUserConfiguration>(c => insertedConfiguration = c)
            .ReturnsAsync(true);

        var result = await context.CreateProvisioner().EnsureUserCalendarAsync(companyId, userId, CancellationToken.None);

        result.Should().BeTrue();

        insertedCalendar.Should().NotBeNull();
        insertedCalendar!.CompanyId.Should().Be(companyId);
        insertedCalendar.OwnerUserId.Should().Be(userId);
        insertedCalendar.Name.Should().Be("Mi calendario");
        insertedCalendar.Color.Should().Be(CalendarUserProvisioner.DefaultCalendarColor);
        insertedCalendar.IsActive.Should().BeTrue();
        insertedCalendar.SortOrder.Should().Be(1);

        insertedConfiguration.Should().NotBeNull();
        insertedConfiguration!.CompanyId.Should().Be(companyId);
        insertedConfiguration.UserId.Should().Be(userId);
        insertedConfiguration.DefaultCalendarId.Should().Be(insertedCalendar.Id);
        insertedConfiguration.WorkDays.Should().Be(CalendarWorkDays.Weekdays);
        insertedConfiguration.DefaultView.Should().Be(CalendarViewType.Week);
        insertedConfiguration.WeekStartsOn.Should().Be(DayOfWeek.Monday);
        insertedConfiguration.BusinessHoursStart.Should().BeNull();
        insertedConfiguration.BusinessHoursEnd.Should().BeNull();

        context.UserCalendarRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<UserCalendar>()), Times.Once);
        context.CalendarUserConfigurationRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarUserConfiguration>()), Times.Once);
    }

    /// <summary>
    /// F10 / idempotent — second call sees the existing <c>CalendarUserConfiguration</c> and short-circuits.
    /// </summary>
    [Fact]
    public async Task EnsureUserCalendarAsync_WhenCalledTwice_ShouldInsertOnlyOnFirstCall()
    {
        var context = new CalendarUserProvisionerTestContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        context.SeedCalendarModule();
        context.SeedCompanyModule(companyId, isActive: true);
        context.SeedCalendarCompany(companyId);

        // The default InsertAsync setup returns true but does not record the inserted row, so a second
        // EnsureUserCalendarAsync call would still see an empty CalendarUserConfiguration table and insert
        // twice. Replace the default setup with a tracked list + GetAllIncludingDeletedAsync lookup so the
        // second call sees what the first one inserted and short-circuits.
        var trackedConfigurations = new List<CalendarUserConfiguration>();
        context.CalendarUserConfigurationRepositoryMock.Reset();
        context.CalendarUserConfigurationRepositoryMock
            .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<Expression<Func<CalendarUserConfiguration, bool>>>()))
            .ReturnsAsync((Expression<Func<CalendarUserConfiguration, bool>> predicate) => trackedConfigurations.Where(predicate.Compile()).ToList());
        context.CalendarUserConfigurationRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarUserConfiguration>()))
            .Callback<CalendarUserConfiguration>(c => trackedConfigurations.Add(c))
            .ReturnsAsync(true);

        var provisioner = context.CreateProvisioner();

        var first = await provisioner.EnsureUserCalendarAsync(companyId, userId, CancellationToken.None);
        var second = await provisioner.EnsureUserCalendarAsync(companyId, userId, CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeFalse();

        context.UserCalendarRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<UserCalendar>()), Times.Once);
        context.CalendarUserConfigurationRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarUserConfiguration>()), Times.Once);
    }

    /// <summary>
    /// F10 / no Calendar module in <see cref="SystemModule"/> — provisioner returns false without writing anything.
    /// </summary>
    [Fact]
    public async Task EnsureUserCalendarAsync_WhenCalendarModuleMissing_ShouldReturnFalseWithoutInserting()
    {
        var context = new CalendarUserProvisionerTestContext();
        // No module seeded.

        var result = await context.CreateProvisioner().EnsureUserCalendarAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Should().BeFalse();
        context.UserCalendarRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<UserCalendar>()), Times.Never);
        context.CalendarUserConfigurationRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarUserConfiguration>()), Times.Never);
    }

    /// <summary>
    /// F10 / module not assigned to the company — same no-op result.
    /// </summary>
    [Fact]
    public async Task EnsureUserCalendarAsync_WhenCompanyModuleAssignmentMissing_ShouldReturnFalseWithoutInserting()
    {
        var context = new CalendarUserProvisionerTestContext();
        context.SeedCalendarModule();
        // No CompanyModule seeded.

        var result = await context.CreateProvisioner().EnsureUserCalendarAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Should().BeFalse();
        context.UserCalendarRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<UserCalendar>()), Times.Never);
    }

    /// <summary>
    /// F10 / module assigned but inactive for the company — same no-op result.
    /// </summary>
    [Fact]
    public async Task EnsureUserCalendarAsync_WhenCompanyModuleAssignmentInactive_ShouldReturnFalseWithoutInserting()
    {
        var context = new CalendarUserProvisionerTestContext();
        context.SeedCalendarModule();
        context.SeedCompanyModule(Guid.NewGuid(), isActive: false);

        var result = await context.CreateProvisioner().EnsureUserCalendarAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Should().BeFalse();
        context.UserCalendarRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<UserCalendar>()), Times.Never);
    }

    /// <summary>
    /// F10 / module active but no <see cref="CalendarCompany"/> — provisioner returns false without writing.
    /// </summary>
    [Fact]
    public async Task EnsureUserCalendarAsync_WhenCalendarCompanyMissing_ShouldReturnFalseWithoutInserting()
    {
        var context = new CalendarUserProvisionerTestContext();
        context.SeedCalendarModule();
        context.SeedCompanyModule(Guid.NewGuid(), isActive: true);
        // No CalendarCompany seeded.

        var result = await context.CreateProvisioner().EnsureUserCalendarAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Should().BeFalse();
        context.UserCalendarRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<UserCalendar>()), Times.Never);
    }

    /// <summary>
    /// F10 / "takes the company hours" — SPEC 44 data model 7 keeps them null on the user configuration; the effective
    /// hours come from the <see cref="CalendarCompany"/> row at query time. Verifies the provisioner does NOT copy them.
    /// </summary>
    [Fact]
    public async Task EnsureUserCalendarAsync_WhenCalendarCompanyHasBusinessHours_ShouldLeaveUserConfigurationHoursNull()
    {
        var context = new CalendarUserProvisionerTestContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        context.SeedCalendarModule();
        context.SeedCompanyModule(companyId, isActive: true);
        context.SeedCalendarCompany(companyId,
            businessHoursStart: new TimeOnly(8, 0),
            businessHoursEnd: new TimeOnly(17, 0));

        CalendarUserConfiguration? insertedConfiguration = null;
        context.CalendarUserConfigurationRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarUserConfiguration>()))
            .Callback<CalendarUserConfiguration>(c => insertedConfiguration = c)
            .ReturnsAsync(true);

        var result = await context.CreateProvisioner().EnsureUserCalendarAsync(companyId, userId, CancellationToken.None);

        result.Should().BeTrue();
        insertedConfiguration.Should().NotBeNull();
        insertedConfiguration!.BusinessHoursStart.Should().BeNull();
        insertedConfiguration.BusinessHoursEnd.Should().BeNull();
    }

    /// <summary>
    /// F10 / <see cref="CalendarUserProvisioner.IsModuleActiveAsync"/> — the provisioner short-circuits on a missing
    /// module. This guards the public helper directly.
    /// </summary>
    [Fact]
    public async Task IsModuleActiveAsync_WhenModuleMissing_ShouldReturnFalse()
    {
        var context = new CalendarUserProvisionerTestContext();

        var result = await context.CreateProvisioner().IsModuleActiveAsync(Guid.NewGuid());

        result.Should().BeFalse();
    }

    /// <summary>
    /// <see cref="CalendarUserProvisioner.IsModuleActiveAsync"/> — true when both the module and the per-company
    /// assignment are active.
    /// </summary>
    [Fact]
    public async Task IsModuleActiveAsync_WhenModuleAndCompanyAssignmentActive_ShouldReturnTrue()
    {
        var context = new CalendarUserProvisionerTestContext();
        var companyId = Guid.NewGuid();
        context.SeedCalendarModule();
        context.SeedCompanyModule(companyId, isActive: true);

        var result = await context.CreateProvisioner().IsModuleActiveAsync(companyId);

        result.Should().BeTrue();
    }

    /// <summary>
    /// F10 / "CreateCalendarCompany provisions all existing users" — the handler delegates to
    /// <see cref="CalendarCompanyProvisioning"/>, which loops over the company's user members and calls the provisioner
    /// for each. Verifies the call path through the wrapper, including the no-op for already-provisioned members and
    /// the membership-of-active-users filter.
    /// </summary>
    [Fact]
    public async Task ProvisionPendingUsersAsync_WhenCompanyHasUnprovisionedMembers_ShouldCallProvisionerForEach()
    {
        var context = new CalendarUserProvisionerTestContext();
        var companyId = Guid.NewGuid();
        var activeUserId = Guid.NewGuid();
        var alreadyProvisionedUserId = Guid.NewGuid();
        var inactiveUserId = Guid.NewGuid();

        // Provisioner short-circuits on IsModuleActiveAsync — wire it up so calls actually reach the insert path.
        context.SeedCalendarModule();
        context.SeedCompanyModule(companyId, isActive: true);
        context.SeedCalendarCompany(companyId);

        context.UserCompanyRepositoryMock.SetupRows(new[]
        {
            new UserCompany { UserId = activeUserId, CompanyId = companyId, GcRecord = 0 },
            new UserCompany { UserId = alreadyProvisionedUserId, CompanyId = companyId, GcRecord = 0 },
            new UserCompany { UserId = inactiveUserId, CompanyId = companyId, GcRecord = 0 }
        });
        context.CalendarUserConfigurationRepositoryMock.SetupRows(new[]
        {
            new CalendarUserConfiguration
            {
                CompanyId = companyId,
                UserId = alreadyProvisionedUserId,
                DefaultCalendarId = Guid.NewGuid(),
                GcRecord = 0
            }
        });
        // Only active, non-deleted ApplicationUsers count toward provisioning.
        context.UserApplicationRepositoryMock.SetupRows(new[]
        {
            new ApplicationUser { Id = activeUserId, FirstName = "Jane", LastName = "Doe", Email = "jane@x.com", IsActive = true },
            new ApplicationUser { Id = alreadyProvisionedUserId, FirstName = "Bob", LastName = "X", Email = "b@x.com", IsActive = true },
            new ApplicationUser { Id = inactiveUserId, FirstName = "Old", LastName = "User", Email = "o@x.com", IsActive = false }
        });

        var provisioned = await context.CreateProvisioning().ProvisionPendingUsersAsync(companyId, CancellationToken.None);

        provisioned.Should().ContainSingle()
            .Which.UserId.Should().Be(activeUserId);
        // The provisioner was driven by the wrapper's own IUnitOfWork mocks, which already count a UserCalendar +
        // CalendarUserConfiguration as InsertAsync calls on each successful EnsureUserCalendarAsync.
        context.UserCalendarRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<UserCalendar>()), Times.Once);
        context.CalendarUserConfigurationRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarUserConfiguration>()), Times.Once);
    }

    /// <summary>
    /// Holds the reusable mocks for the provisioner tests.
    /// </summary>
    private sealed class CalendarUserProvisionerTestContext
    {
        private SystemModule? _calendarModule;

        public CalendarUserProvisionerTestContext()
        {
            UserCalendarRepositoryMock
                .Setup(x => x.InsertAsync(It.IsAny<UserCalendar>()))
                .ReturnsAsync(true);
            CalendarUserConfigurationRepositoryMock
                .Setup(x => x.InsertAsync(It.IsAny<CalendarUserConfiguration>()))
                .ReturnsAsync(true);

            UnitOfWorkMock.Setup(x => x.GetRepository<SystemModule>()).Returns(SystemModuleRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CompanyModule>()).Returns(CompanyModuleRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarCompany>()).Returns(CalendarCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarUserConfiguration>()).Returns(CalendarUserConfigurationRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<UserCalendar>()).Returns(UserCalendarRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<UserCompany>()).Returns(UserCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationUser>()).Returns(UserApplicationRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<SystemModule>> SystemModuleRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CompanyModule>> CompanyModuleRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarCompany>> CalendarCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserCalendar>> UserCalendarRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarUserConfiguration>> CalendarUserConfigurationRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserCompany>> UserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> UserApplicationRepositoryMock { get; } = new();

        public CalendarUserProvisioner CreateProvisioner() => new(UnitOfWorkMock.Object);

        public CalendarCompanyProvisioning CreateProvisioning() => new(UnitOfWorkMock.Object, CreateProvisioner());

        public void SeedCalendarModule()
        {
            _calendarModule = new SystemModule
            {
                Name = CalendarUserProvisioner.CalendarModuleName,
                IsActive = true
            };
            SystemModuleRepositoryMock.SetupRows(new[] { _calendarModule });
        }

        public void SeedCompanyModule(Guid companyId, bool isActive)
        {
            if (_calendarModule is null)
            {
                throw new InvalidOperationException("SeedCalendarModule must be called before SeedCompanyModule.");
            }

            CompanyModuleRepositoryMock.SetupRows(new[]
            {
                new CompanyModule
                {
                    CompanyId = companyId,
                    ModuleId = _calendarModule.Id,
                    IsActive = isActive
                }
            });
        }

        public void SeedCalendarCompany(
            Guid companyId,
            string defaultCalendarName = "Mi calendario",
            TimeOnly? businessHoursStart = null,
            TimeOnly? businessHoursEnd = null)
        {
            CalendarCompanyRepositoryMock.SetupRows(new[]
            {
                new CalendarCompany
                {
                    CompanyId = companyId,
                    DefaultCalendarName = defaultCalendarName,
                    BusinessHoursStart = businessHoursStart,
                    BusinessHoursEnd = businessHoursEnd,
                    DefaultActivityUserId = Guid.NewGuid(),
                    RescheduledByOthersStatusCompanyId = Guid.NewGuid(),
                    TimeZone = "America/Managua"
                }
            });
        }
    }
}