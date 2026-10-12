// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Linq.Expressions;
using System.Reflection;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.Common;

/// <summary>
/// SPEC 44 F10 / Guard group — exercises <see cref="CalendarCompanyGuard"/> end-to-end with mocked
/// <see cref="IUnitOfWork"/>. The guard is the single gate a company-scoped handler hits before doing any
/// calendar work; its four cases are listed in the spec: empty <c>CompanyId</c> → error,
/// missing <c>CalendarCompany</c> row → <c>CALENDAR_COMPANY_NOT_CONFIGURED</c>, full context when the row
/// exists, and the design-level guarantee that the guard does not gate global catalog queries
/// (no overload / method exists for them).
/// </summary>
public sealed class CalendarCompanyGuardTests
{
    private const string TimeZoneId = "America/Managua";

    /// <summary>
    /// Calling without a tenant context (<c>Guid.Empty</c>) returns an unsuccessful response; no row is
    /// ever going to match an empty <c>CompanyId</c>, so the guard fails closed the same way it does for an
    /// unconfigured company.
    /// </summary>
    [Fact]
    public async Task GetContextAsync_WhenCompanyIdIsEmpty_ShouldReturnError()
    {
        var context = new CalendarCompanyGuardTestContext();

        var response = await context.CreateGuard().GetContextAsync(Guid.Empty, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Data.Should().BeNull();
        context.CalendarCompanyRepositoryMock.Verify(
            x => x.GetAllIncludingDeletedAsync(It.IsAny<Expression<Func<CalendarCompany, bool>>>()),
            Times.Once);
    }

    /// <summary>
    /// A real tenant that has not yet configured its calendar answers with
    /// <see cref="CalendarErrorCodes.CompanyNotConfigured"/> (<c>CALENDAR_COMPANY_NOT_CONFIGURED</c>).
    /// </summary>
    [Fact]
    public async Task GetContextAsync_WhenCalendarCompanyIsMissing_ShouldReturnNotConfigured()
    {
        var context = new CalendarCompanyGuardTestContext();
        var companyId = Guid.NewGuid();

        var response = await context.CreateGuard().GetContextAsync(companyId, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Data.Should().BeNull();
        response.Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
    }

    /// <summary>
    /// When the company has a configured row, the guard projects every field of <see cref="CalendarCompanyContext"/>
    /// — including the resolved <see cref="TimeZoneInfo"/> and the optional business hours.
    /// </summary>
    [Fact]
    public async Task GetContextAsync_WhenCalendarCompanyExists_ShouldReturnFullContext()
    {
        var context = new CalendarCompanyGuardTestContext();
        var companyId = Guid.NewGuid();
        var defaultUserId = Guid.NewGuid();
        var rescheduledStatusId = Guid.NewGuid();
        var calendarCompanyId = Guid.NewGuid();
        var calendarCompany = new CalendarCompany
        {
            CompanyId = companyId,
            DefaultActivityUserId = defaultUserId,
            TimeZone = TimeZoneId,
            IsOpen24Hours = false,
            BusinessHoursStart = new TimeOnly(8, 0),
            BusinessHoursEnd = new TimeOnly(17, 30),
            DefaultCalendarName = "Mi calendario",
            RescheduledByOthersStatusCompanyId = rescheduledStatusId
        };
        EntityIdSetter.SetId(calendarCompany, calendarCompanyId);
        context.SetCalendarCompanyRow(calendarCompany);

        var response = await context.CreateGuard().GetContextAsync(companyId, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().BeEmpty();
        var data = response.Data;
        data.Should().NotBeNull();
        data!.CompanyId.Should().Be(companyId);
        data.CalendarCompanyId.Should().Be(calendarCompanyId);
        data.TimeZoneId.Should().Be(TimeZoneId);
        data.TimeZone.Should().Be(TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId));
        data.IsOpen24Hours.Should().BeFalse();
        data.BusinessHoursStart.Should().Be(new TimeOnly(8, 0));
        data.BusinessHoursEnd.Should().Be(new TimeOnly(17, 30));
        data.DefaultActivityUserId.Should().Be(defaultUserId);
        data.DefaultCalendarName.Should().Be("Mi calendario");
        data.RescheduledByOthersStatusCompanyId.Should().Be(rescheduledStatusId);
    }

    /// <summary>
    /// The guard's only public surface is the company-scoped <c>GetContextAsync(Guid, CancellationToken)</c>:
    /// global catalog queries (<c>CalendarActivity</c>, <c>CalendarStatus</c>) are not enforced by this guard and
    /// therefore do not need to invoke it — its absence from their handlers is intentional, not an oversight.
    /// </summary>
    [Fact]
    public void GuardApi_ShouldOnlyExposeCompanyScopedGetContextAsync()
    {
        var guardMethods = typeof(CalendarCompanyGuard)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => new { m.Name, Parameters = m.GetParameters().Select(p => p.ParameterType).ToArray() })
            .ToList();

        guardMethods.Should().ContainSingle();
        guardMethods[0].Name.Should().Be(nameof(CalendarCompanyGuard.GetContextAsync));
        guardMethods[0].Parameters.Should().Equal(typeof(Guid), typeof(CancellationToken));
    }

    /// <summary>
    /// Holds the reusable mocks for the guard tests. <see cref="GenericRepositoryMockExtensions.SetupRows{T}"/>
    /// compiles the handler's <c>GcRecord == 0</c> + <c>CompanyId == @TenantId</c> predicate in memory, matching
    /// what EF Core would emit against SQL Server in production.
    /// </summary>
    private sealed class CalendarCompanyGuardTestContext
    {
        private readonly List<CalendarCompany> _rows = new();

        public CalendarCompanyGuardTestContext()
        {
            CalendarCompanyRepositoryMock.SetupRows(_rows);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarCompany>())
                .Returns(CalendarCompanyRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarCompany>> CalendarCompanyRepositoryMock { get; } = new();

        /// <summary>
        /// Adds the configured <see cref="CalendarCompany"/> row so the guard's predicate resolves it.
        /// Re-runs <see cref="GenericRepositoryMockExtensions.SetupRows{T}"/> after mutating the backing list
        /// because the helper snapshots the rows at the time it is called.
        /// </summary>
        public void SetCalendarCompanyRow(CalendarCompany calendarCompany)
        {
            _rows.Clear();
            _rows.Add(calendarCompany);
            CalendarCompanyRepositoryMock.SetupRows(_rows);
        }

        public CalendarCompanyGuard CreateGuard() => new(UnitOfWorkMock.Object);
    }
}
