using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Finance;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Domain.Common;
using JOIN.Domain.Finance;
using JOIN.Persistence.Contexts;
using JOIN.Persistence.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Finance;

/// <summary>
/// Finance test bed (SPEC 56): InMemory <see cref="ApplicationDbContext"/>, the real <see cref="UnitOfWork"/> and the
/// Application services. Seeds the DOP, USD and EUR global currencies and a company whose base currency is DOP.
/// <see cref="AmountSource"/> is a registered <see cref="IBaseCurrencyAmountSource"/> the tests switch on and off.
/// </summary>
internal sealed class FinanceTestContext : IDisposable
{
    private readonly Company _company = new() { Name = "Finance Co", TaxId = "FIN-1", IsActive = true };

    public Guid CompanyId => _company.Id;
    public Dictionary<string, Guid> Currency { get; } = [];
    public Guid DopCompanyCurrencyId { get; private set; }

    public Mock<ICurrentUserService> CurrentUser { get; } = new();
    public ApplicationDbContext Db { get; }
    public ServiceProvider Services { get; }
    public FakeAmountSource AmountSource { get; } = new();

    public Guid ActingCompanyId { get; set; }
    public bool IsSuperAdmin { get; set; }

    private FinanceTestContext()
    {
        CurrentUser.SetupGet(x => x.CompanyId).Returns(() => ActingCompanyId);
        CurrentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
        CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        CurrentUser.Setup(x => x.IsInRole("SuperAdmin")).Returns(() => IsSuperAdmin);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"finance-tests-{Guid.NewGuid()}")
            .Options;
        Db = new ApplicationDbContext(options, new AuditableEntitySaveChangesInterceptor(CurrentUser.Object), CurrentUser.Object);

        var connectionFactory = new Mock<ISqlConnectionFactory>();
        var unitOfWork = new LockFreeUnitOfWork(new UnitOfWork(Db, new DapperContext(new ConfigurationBuilder().Build()), connectionFactory.Object));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.Configure<PaginationSettings>(_ => { });
        services.AddApplicationServices();
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        services.AddSingleton(CurrentUser.Object);
        services.AddSingleton(new Mock<IPermissionService>().Object);
        services.AddSingleton(new Mock<ICompanyCatalogSeeder>().Object);
        services.AddSingleton(new Mock<IDatabaseErrorClassifier>().Object);
        services.AddSingleton(connectionFactory.Object);
        // Only the switchable source: the real ones (SPEC 57 products) query with Dapper, not available here.
        services.RemoveAll<IBaseCurrencyAmountSource>();
        services.AddSingleton<IBaseCurrencyAmountSource>(AmountSource);
        Services = services.BuildServiceProvider();
    }

    public static async Task<FinanceTestContext> CreateAsync()
    {
        var context = new FinanceTestContext();
        context.ActingCompanyId = context.CompanyId;
        await context.SeedAsync();
        return context;
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Runs a request through its handler (no pipeline behaviors).</summary>
    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(request.GetType(), typeof(TResponse));
        dynamic handler = Services.GetRequiredService(handlerType);
        return (Task<TResponse>)handler.Handle((dynamic)request, CancellationToken.None);
    }

    public async Task<List<CompanyCurrency>> CompanyCurrenciesAsync()
    {
        Db.ChangeTracker.Clear();
        return await Db.CompanyCurrencies.IgnoreQueryFilters().Where(c => c.CompanyId == CompanyId).ToListAsync();
    }

    /// <summary>Removes the company's base currency (to test <c>BASE_CURRENCY_REQUIRED</c>).</summary>
    public async Task RemoveBaseCurrencyAsync()
    {
        var row = await Db.CompanyCurrencies.IgnoreQueryFilters().SingleAsync(c => c.Id == DopCompanyCurrencyId);
        row.MarkAsDeleted();
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public async Task<Guid> AddCompanyCurrencyAsync(string code, bool isBase = false, bool isActive = true)
    {
        var row = new CompanyCurrency { CompanyId = CompanyId, CurrencyId = Currency[code], IsBase = isBase, IsActive = isActive };
        Db.CompanyCurrencies.Add(row);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return row.Id;
    }

    private async Task SeedAsync()
    {
        Db.Companies.Add(_company);
        foreach (var code in new[] { "DOP", "USD", "EUR" })
        {
            var currency = new Currency { Code = code, Name = code, Symbol = code, Decimals = 2, IsActive = true };
            Currency[code] = currency.Id;
            Db.Currencies.Add(currency);
        }

        var dop = new CompanyCurrency { CompanyId = CompanyId, CurrencyId = Currency["DOP"], IsBase = true, IsActive = true };
        DopCompanyCurrencyId = dop.Id;
        Db.CompanyCurrencies.Add(dop);

        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        Services.Dispose();
        Db.Dispose();
    }

    /// <summary>Amount source the tests turn on to simulate a company that already stores amounts.</summary>
    internal sealed class FakeAmountSource : IBaseCurrencyAmountSource
    {
        public bool HasAmounts { get; set; }
        public string Name => "FakeAmounts";
        public Task<bool> HasAmountsAsync(Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(HasAmounts);
    }
}
