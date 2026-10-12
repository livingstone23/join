using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using JOIN.Infrastructure.Calendars;
using JOIN.Persistence.Contexts;
using JOIN.Persistence.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;

/// <summary>
/// Calendar test bed (SPEC 44): InMemory <see cref="ApplicationDbContext"/>, the real <see cref="UnitOfWork"/>
/// without the schedule lock, the real Ical.Net expander and the Application services from
/// <c>AddApplicationServices</c>. Seeds a company in <c>America/Managua</c> (UTC-6, no DST) open 08:00–17:00 with the
/// five statuses, three activity types (MEETING: no overlap + business hours, TASK: overlap + business hours,
/// REMINDER: overlap, any time), the WEB channel and two provisioned users (Owner = current user, Other).
/// </summary>
internal sealed class CalendarTestContext : IDisposable
{
    private readonly Company _company = new() { Name = "Calendar Co", TaxId = "CAL-1", IsActive = true };

    public Guid CompanyId => _company.Id;
    public Guid OwnerId { get; } = Guid.NewGuid();
    public Guid OtherId { get; } = Guid.NewGuid();
    public Guid StrangerId { get; } = Guid.NewGuid();
    public Guid WebChannelId { get; private set; }
    public Guid CalendarModuleId { get; private set; }

    public Dictionary<string, Guid> GlobalStatus { get; } = [];
    public Dictionary<string, Guid> Status { get; } = [];
    public Dictionary<string, Guid> GlobalActivity { get; } = [];
    public Dictionary<string, Guid> Activity { get; } = [];
    public Dictionary<Guid, Guid> DefaultCalendar { get; } = [];

    public Mock<ICurrentUserService> CurrentUser { get; } = new();
    public Mock<IPermissionService> Permissions { get; } = new();
    public Mock<ICompanyCatalogSeeder> CatalogSeeder { get; } = new();
    public SequencedFakeDbConnection Connection { get; } = new();
    public ApplicationDbContext Db { get; }
    public LockFreeUnitOfWork UnitOfWork { get; }
    public ServiceProvider Services { get; }

    private CalendarTestContext()
    {
        CurrentUser.SetupGet(x => x.CompanyId).Returns(() => ActingCompanyId);
        CurrentUser.SetupGet(x => x.UserId).Returns(() => ActingUserId?.ToString());
        CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"calendar-tests-{Guid.NewGuid()}")
            .Options;
        Db = new ApplicationDbContext(options, new AuditableEntitySaveChangesInterceptor(CurrentUser.Object), CurrentUser.Object);

        var connectionFactory = new Mock<ISqlConnectionFactory>();
        connectionFactory.Setup(x => x.CreateConnection()).Returns(() => Connection);
        UnitOfWork = new LockFreeUnitOfWork(new UnitOfWork(Db, new DapperContext(new ConfigurationBuilder().Build()), connectionFactory.Object));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.Configure<PaginationSettings>(_ => { });
        services.AddApplicationServices();
        services.AddSingleton<IUnitOfWork>(UnitOfWork);
        services.AddSingleton(CurrentUser.Object);
        services.AddSingleton(Permissions.Object);
        services.AddSingleton(CatalogSeeder.Object);
        services.AddSingleton(connectionFactory.Object);
        services.AddSingleton<ICalendarRecurrenceExpander, IcalRecurrenceExpander>();
        Services = services.BuildServiceProvider();
    }

    public Guid ActingCompanyId { get; set; }
    public Guid? ActingUserId { get; set; }

    /// <summary>
    /// Builds the context. <paramref name="configureCompany"/> creates <c>CalendarCompany</c> and provisions both users.
    /// </summary>
    public static async Task<CalendarTestContext> CreateAsync(bool configureCompany = true, bool moduleActive = true, bool open24Hours = false)
    {
        var context = new CalendarTestContext();
        context.ActingCompanyId = context.CompanyId;
        context.ActingUserId = context.OwnerId;
        await context.SeedAsync(configureCompany, moduleActive, open24Hours);
        return context;
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary> Runs a request through its handler (no pipeline behaviors). </summary>
    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(request.GetType(), typeof(TResponse));
        dynamic handler = Services.GetRequiredService(handlerType);
        return (Task<TResponse>)handler.Handle((dynamic)request, CancellationToken.None);
    }

    /// <summary> Local time of the company (UTC-6) as an offset. </summary>
    public static DateTimeOffset Local(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(-6));

    /// <summary> UTC instant of a local time of the company. </summary>
    public static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        Local(year, month, day, hour, minute).UtcDateTime;

    public async Task<CalendarEvent> ReloadEventAsync(Guid id)
    {
        Db.ChangeTracker.Clear();
        return await Db.CalendarEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == id);
    }

    public async Task<List<CalendarEventLog>> LogsAsync(Guid eventId) =>
        await Db.CalendarEventLogs.IgnoreQueryFilters().Where(l => l.CalendarEventId == eventId).OrderBy(l => l.OccurredUtc).ToListAsync();

    private async Task SeedAsync(bool configureCompany, bool moduleActive, bool open24Hours)
    {
        Db.Companies.Add(_company);

        var module = new SystemModule { Name = CalendarUserProvisioner.CalendarModuleName, IsActive = true };
        CalendarModuleId = module.Id;
        Db.SystemModules.Add(module);
        if (moduleActive)
        {
            Db.CompanyModules.Add(new CompanyModule { CompanyId = CompanyId, ModuleId = module.Id, IsActive = true });
        }

        var web = new CommunicationChannel { Name = "Web", Code = CommunicationChannelCodes.Web, Provider = "Internal", IsActive = true };
        WebChannelId = web.Id;
        Db.CommunicationChannels.Add(web);

        foreach (var (userId, name) in new[] { (OwnerId, "Owner"), (OtherId, "Other"), (StrangerId, "Stranger") })
        {
            Db.ApplicationUsers.Add(new ApplicationUser { Id = userId, FirstName = name, LastName = "User", Email = $"{name.ToLowerInvariant()}@t.local", UserName = $"{name}@t.local", IsActive = true });
        }

        Db.UserCompanies.Add(new UserCompany { UserId = OwnerId, CompanyId = CompanyId, IsDefault = true });
        Db.UserCompanies.Add(new UserCompany { UserId = OtherId, CompanyId = CompanyId, IsDefault = true });

        var statuses = new (string Code, bool Blocks, bool Final)[]
        {
            (CalendarCodes.Statuses.Pending, true, false),
            (CalendarCodes.Statuses.Confirmed, true, false),
            (CalendarCodes.Statuses.Rescheduled, true, false),
            (CalendarCodes.Statuses.Cancelled, false, true),
            (CalendarCodes.Statuses.Completed, true, true)
        };
        foreach (var (code, blocks, final) in statuses)
        {
            var global = new CalendarStatus { Code = code, Name = code, Color = "#000000", BlocksTime = blocks, IsFinal = final, IsActive = true };
            var company = new CalendarStatusCompany { CompanyId = CompanyId, CalendarStatusId = global.Id, Name = code.ToLowerInvariant(), Color = "#111111", IsActive = true };
            GlobalStatus[code] = global.Id;
            Status[code] = company.Id;
            Db.CalendarStatuses.Add(global);
            Db.CalendarStatusCompanies.Add(company);
        }

        var activities = new (string Code, int Minutes, bool Overlap, bool BusinessHours)[]
        {
            (CalendarCodes.Activities.Meeting, 60, false, true),
            (CalendarCodes.Activities.Task, 30, true, true),
            (CalendarCodes.Activities.Reminder, 15, true, false),
            (CalendarCodes.Activities.PersonReview, 15, true, false)
        };
        foreach (var (code, minutes, overlap, businessHours) in activities)
        {
            var global = new CalendarActivity { Code = code, Name = code, Color = "#222222", DefaultDurationMinutes = minutes, AllowsOverlap = overlap, RequiresBusinessHours = businessHours, IsActive = true };
            var company = new CalendarActivityCompany
            {
                CompanyId = CompanyId,
                CalendarActivityId = global.Id,
                Name = code.ToLowerInvariant(),
                Color = "#333333",
                DefaultDurationMinutes = minutes,
                AllowsOverlap = overlap,
                RequiresBusinessHours = businessHours,
                InitialCalendarStatusCompanyId = Status[CalendarCodes.Statuses.Pending],
                IsActive = true
            };
            GlobalActivity[code] = global.Id;
            Activity[code] = company.Id;
            Db.CalendarActivities.Add(global);
            Db.CalendarActivityCompanies.Add(company);
        }

        if (configureCompany)
        {
            Db.CalendarCompanies.Add(new CalendarCompany
            {
                CompanyId = CompanyId,
                DefaultActivityUserId = OwnerId,
                TimeZone = "America/Managua",
                IsOpen24Hours = open24Hours,
                BusinessHoursStart = open24Hours ? null : new TimeOnly(8, 0),
                BusinessHoursEnd = open24Hours ? null : new TimeOnly(17, 0),
                DefaultCalendarName = "Mi calendario",
                RescheduledByOthersStatusCompanyId = Status[CalendarCodes.Statuses.Rescheduled]
            });

            foreach (var userId in new[] { OwnerId, OtherId })
            {
                var calendar = new UserCalendar { CompanyId = CompanyId, OwnerUserId = userId, Name = "Mi calendario", Color = "#1E88E5", IsActive = true };
                DefaultCalendar[userId] = calendar.Id;
                Db.UserCalendars.Add(calendar);
                Db.CalendarUserConfigurations.Add(new CalendarUserConfiguration
                {
                    CompanyId = CompanyId,
                    UserId = userId,
                    DefaultCalendarId = calendar.Id,
                    WorkDays = CalendarWorkDays.Weekdays
                });
            }
        }

        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        Services.Dispose();
        Db.Dispose();
    }
}
