using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Customers;
using JOIN.Application.Interface.Finance;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Domain.Admin;
using JOIN.Domain.Catalog;
using JOIN.Domain.Common;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Enums;
using JOIN.Domain.Finance;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using JOIN.Persistence.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MasterContactType = JOIN.Domain.Customers.Leads.ContactType;

namespace JOIN.Application.UnitTest.UseCases.Customers.Leads;

/// <summary>
/// Lead test bed (SPEC 61): InMemory context, real unit of work without advisory locks, Application services, and fakes
/// for the two Dapper reads the commands use (exchange rates and the lead scope). Seeds a company with the commercial
/// settings, DOP base + USD (rate 60), two products, two users bound to the company and two persons.
/// </summary>
internal sealed class LeadTestContext : IDisposable
{
    private readonly Company _company = new() { Name = "Lead Co", TaxId = "LEAD-1", IsActive = true };

    public Guid CompanyId => _company.Id;
    public Guid UserId { get; } = Guid.NewGuid();
    public Guid OtherUserId { get; } = Guid.NewGuid();
    public Guid PersonId { get; private set; }
    public Guid OtherPersonId { get; private set; }
    public Guid GenderId { get; private set; }
    private readonly Currency _dop = new() { Code = "DOP", Name = "Peso" };
    private readonly Currency _usd = new() { Code = "USD", Name = "Dólar" };
    public Guid Dop => _dop.Id;
    public Guid Usd => _usd.Id;
    public Dictionary<string, LeadStage> Stage { get; } = [];
    public Dictionary<string, Guid> Source { get; } = [];
    public Dictionary<string, Guid> Reason { get; } = [];
    public Dictionary<string, Guid> Relationship { get; } = [];
    public Dictionary<string, Guid> ContactType { get; } = [];
    public Dictionary<string, Product> Product { get; } = [];

    public Mock<ICurrentUserService> CurrentUser { get; } = new();
    public ApplicationDbContext Db { get; }
    public LockFreeUnitOfWork UnitOfWork { get; }
    public ServiceProvider Services { get; }
    public FakeRates Rates { get; } = new();
    public FakeScopes Scopes { get; } = new();
    public FakeStorage Storage { get; } = new();

    public Guid ActingCompanyId { get; set; }
    public string? ActingUserId { get; set; }
    public bool IsSuperAdmin { get; set; }

    private LeadTestContext()
    {
        CurrentUser.SetupGet(x => x.CompanyId).Returns(() => ActingCompanyId);
        CurrentUser.SetupGet(x => x.UserId).Returns(() => ActingUserId);
        CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        CurrentUser.Setup(x => x.IsInRole("SuperAdmin")).Returns(() => IsSuperAdmin);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"lead-tests-{Guid.NewGuid()}").Options;
        Db = new ApplicationDbContext(options, new AuditableEntitySaveChangesInterceptor(CurrentUser.Object), CurrentUser.Object);
        var connectionFactory = new Mock<ISqlConnectionFactory>();
        UnitOfWork = new LockFreeUnitOfWork(new UnitOfWork(Db, new DapperContext(new ConfigurationBuilder().Build()), connectionFactory.Object));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.Configure<PaginationSettings>(_ => { });
        services.AddApplicationServices();
        services.AddSingleton<IUnitOfWork>(UnitOfWork);
        services.AddSingleton(CurrentUser.Object);
        services.AddSingleton(new Mock<IPermissionService>().Object);
        services.AddSingleton(new Mock<ICompanyCatalogSeeder>().Object);
        services.AddSingleton(new Mock<IDatabaseErrorClassifier>().Object);
        services.AddSingleton(connectionFactory.Object);
        // CreatePerson (NewPerson) needs the address coordinator's repository, normally registered by Persistence.
        services.AddSingleton(UnitOfWork.PersonAddresses);
        services.RemoveAll<IExchangeRateResolver>();
        services.AddSingleton<IExchangeRateResolver>(Rates);
        services.AddSingleton<IFileStorageResolver>(Storage);
        services.RemoveAll<ILeadAccessScopeResolver>();
        services.AddSingleton<ILeadAccessScopeResolver>(Scopes);
        Services = services.BuildServiceProvider();
    }

    public static async Task<LeadTestContext> CreateAsync()
    {
        var context = new LeadTestContext();
        context.ActingCompanyId = context.CompanyId;
        context.ActingUserId = context.UserId.ToString();
        await context.SeedAsync();
        return context;
    }

    public void ActAs(Guid userId) => ActingUserId = userId.ToString();

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary> Runs a request through its handler (no pipeline behaviors). </summary>
    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(request.GetType(), typeof(TResponse));
        dynamic handler = Services.GetRequiredService(handlerType);
        return (Task<TResponse>)handler.Handle((dynamic)request, CancellationToken.None);
    }

    public async Task<Lead> LeadAsync(Guid id)
    {
        Db.ChangeTracker.Clear();
        return await Db.Leads.IgnoreQueryFilters().SingleAsync(l => l.Id == id);
    }

    public async Task<List<T>> RowsAsync<T>(Func<T, bool> predicate) where T : class
    {
        Db.ChangeTracker.Clear();
        return (await Db.Set<T>().IgnoreQueryFilters().ToListAsync()).Where(predicate).ToList();
    }

    public async Task<Guid> AddCompanyUserAsync(string name)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), FirstName = name, LastName = "Test", Email = $"{name}@t.local", UserName = $"{name}@t.local", IsActive = true };
        Db.ApplicationUsers.Add(user);
        Db.UserCompanies.Add(new UserCompany { UserId = user.Id, CompanyId = CompanyId });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return user.Id;
    }

    private async Task SeedAsync()
    {
        Db.Companies.Add(_company);
        foreach (var (id, name) in new[] { (UserId, "ana"), (OtherUserId, "luis") })
        {
            Db.ApplicationUsers.Add(new ApplicationUser { Id = id, FirstName = name, LastName = "User", Email = $"{name}@t.local", UserName = $"{name}@t.local", IsActive = true });
            Db.UserCompanies.Add(new UserCompany { UserId = id, CompanyId = CompanyId });
        }

        var gender = Gender.Create(CompanyId, "F", "Femenino");
        GenderId = gender.Id;
        Db.Add(gender);
        var idType = new IdentificationType { Name = "Cedula" };
        Db.IdentificationTypes.Add(idType);
        var person = new Person { CompanyId = CompanyId, PersonType = PersonType.Physical, FirstName = "Livingstone", LastName = "Cano", IdentificationTypeId = idType.Id, IdentificationNumber = "001" };
        var other = new Person { CompanyId = CompanyId, PersonType = PersonType.Legal, FirstName = "Norma", LastName = "Osorio", CommercialName = "Osorio SRL" };
        PersonId = person.Id;
        OtherPersonId = other.Id;
        Db.Persons.AddRange(person, other);

        var order = 0;
        foreach (var (code, kind, probability) in new[] { ("NEW", LeadStageKind.Initial, 10), ("CONTACTED", LeadStageKind.Open, 25), ("QUALIFIED", LeadStageKind.Open, 40), ("PROPOSAL", LeadStageKind.Open, 60), ("WON", LeadStageKind.Won, 100), ("LOST", LeadStageKind.Lost, 0) })
        {
            var stage = new LeadStage { CompanyId = CompanyId, Code = code, Name = code, Kind = kind, Probability = probability, SortOrder = ++order };
            Stage[code] = stage;
            Db.LeadStages.Add(stage);
        }

        foreach (var code in new[] { "WEB", "REFERRAL" })
        {
            var source = new LeadSource { CompanyId = CompanyId, Code = code, Name = code };
            Source[code] = source.Id;
            Db.LeadSources.Add(source);
        }

        foreach (var (code, usage) in new[] { ("NO_ANSWER", LeadReasonUsage.Inactive), ("HIGH_PRICE", LeadReasonUsage.Lost) })
        {
            var reason = new LeadReason { CompanyId = CompanyId, Code = code, Name = code, UsedFor = usage };
            Reason[code] = reason.Id;
            Db.LeadReasons.Add(reason);
        }

        var relationship = new RelationshipType { CompanyId = CompanyId, Code = "PROSPECT", Name = "Posible cliente", OnWin = RelationshipOnWin.Customer };
        Relationship["PROSPECT"] = relationship.Id;
        Db.RelationshipTypes.Add(relationship);

        foreach (var (code, asksDuration) in new[] { ("CALL", true), ("EMAIL", false) })
        {
            var master = new MasterContactType { Code = code, Name = code, Icon = "x", AsksDuration = asksDuration };
            var copy = new ContactTypeCompany { CompanyId = CompanyId, ContactTypeId = master.Id, Name = code };
            ContactType[code] = copy.Id;
            Db.ContactTypes.Add(master);
            Db.ContactTypeCompanies.Add(copy);
        }

        Db.Currencies.AddRange(_dop, _usd);
        Db.CompanyCurrencies.AddRange(
            new CompanyCurrency { CompanyId = CompanyId, CurrencyId = Dop, IsBase = true },
            new CompanyCurrency { CompanyId = CompanyId, CurrencyId = Usd });
        Rates.Set(Dop, 1m, isBase: true);
        Rates.Set(Usd, 60m);

        foreach (var (code, price, active) in new[] { ("CRM", 1200m, true), ("POS", 6000m, true), ("OLD", 10m, false) })
        {
            var product = new Product { CompanyId = CompanyId, Code = code, Name = code, ReferencePrice = price, IsActive = active, Kind = ProductKind.Service };
            Product[code] = product;
            Db.Products.Add(product);
        }

        Db.CustomerStages.Add(JOIN.Domain.Customers.CustomerStage.Create(CompanyId, JOIN.Domain.Customers.CustomerStage.Codes.Customer, "Cliente", null, 1, isDefault: true));
        foreach (var (code, usage) in new[] { ("BEST_PRICE", LeadReasonUsage.Won) })
        {
            var reason = new LeadReason { CompanyId = CompanyId, Code = code, Name = code, UsedFor = usage };
            Reason[code] = reason.Id;
            Db.LeadReasons.Add(reason);
        }

        var supplier = new RelationshipType { CompanyId = CompanyId, Code = "SUPPLIER", Name = "Proveedor", OnWin = RelationshipOnWin.None };
        Relationship["SUPPLIER"] = supplier.Id;
        Db.RelationshipTypes.Add(supplier);
        IdentificationTypeId = idType.Id;

        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public Guid IdentificationTypeId { get; private set; }

    public void Dispose()
    {
        Services.Dispose();
        Db.Dispose();
    }

    /// <summary>Rates by currency; missing currency → no rate.</summary>
    internal sealed class FakeRates : IExchangeRateResolver
    {
        private readonly Dictionary<Guid, ExchangeRateResolution> _rates = [];

        public void Set(Guid currencyId, decimal rate, bool isBase = false) =>
            _rates[currencyId] = new ExchangeRateResolution(rate, isBase ? null : DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), isBase);

        public void Remove(Guid currencyId) => _rates.Remove(currencyId);

        public Task<ExchangeRateResolution?> ResolveAsync(Guid companyId, Guid currencyId, DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rates.TryGetValue(currencyId, out var rate) ? rate : null);
    }

    /// <summary>In-memory storage acting as the configured provider (Local).</summary>
    internal sealed class FakeStorage : IFileStorageResolver, IFileStorageService
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public bool Missing { get; set; }

        public IFileStorageService Current => this;
        public IFileStorageService? Resolve(StorageProviderKind kind) => kind == StorageProviderKind.Local ? this : null;
        public StorageProviderKind Kind => StorageProviderKind.Local;

        public async Task<FileStorageSaveResult> SaveAsync(Stream content, string storageKey, string contentType, CancellationToken cancellationToken)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            Files[storageKey] = memory.ToArray();
            return new FileStorageSaveResult(storageKey, memory.Length);
        }

        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(!Missing && Files.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes) : null);

        public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken) => Task.FromResult(Files.Remove(storageKey));
    }

    /// <summary>Scope per user; default: own leads only.</summary>
    internal sealed class FakeScopes : ILeadAccessScopeResolver
    {
        private readonly Dictionary<Guid, LeadAccessScope> _scopes = [];

        public void Set(LeadAccessScope scope) => _scopes[scope.UserId] = scope;

        public Task<LeadAccessScope> ResolveAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_scopes.TryGetValue(userId, out var scope) ? scope : new LeadAccessScope(userId, false, [], [], false));
    }
}
