using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Customers;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using JOIN.Persistence.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Customers;

/// <summary>
/// Customer test bed (SPEC 55): InMemory <see cref="ApplicationDbContext"/>, the real <see cref="UnitOfWork"/> without
/// the advisory lock and the Application services. Seeds a company with the three post-sale stages (CUSTOMER default, SPEC 63),
/// a user (the current one) and two active persons.
/// </summary>
internal sealed class CustomerTestContext : IDisposable
{
    private readonly Company _company = new() { Name = "Customer Co", TaxId = "CUS-1", IsActive = true };

    public Guid CompanyId => _company.Id;
    public Guid UserId { get; } = Guid.NewGuid();
    public Guid PersonId { get; private set; }
    public Guid OtherPersonId { get; private set; }
    public Dictionary<string, Guid> Stage { get; } = [];

    public Mock<ICurrentUserService> CurrentUser { get; } = new();
    public ApplicationDbContext Db { get; }
    public LockFreeUnitOfWork UnitOfWork { get; }
    public ServiceProvider Services { get; }

    public Guid ActingCompanyId { get; set; }
    public string? ActingUserId { get; set; }
    public bool IsSuperAdmin { get; set; }

    private CustomerTestContext()
    {
        CurrentUser.SetupGet(x => x.CompanyId).Returns(() => ActingCompanyId);
        CurrentUser.SetupGet(x => x.UserId).Returns(() => ActingUserId);
        CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        CurrentUser.Setup(x => x.IsInRole("SuperAdmin")).Returns(() => IsSuperAdmin);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"customer-tests-{Guid.NewGuid()}")
            .Options;
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
        Services = services.BuildServiceProvider();
    }

    public static async Task<CustomerTestContext> CreateAsync()
    {
        var context = new CustomerTestContext();
        context.ActingCompanyId = context.CompanyId;
        context.ActingUserId = context.UserId.ToString();
        await context.SeedAsync();
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

    public async Task<List<Customer>> CustomersAsync()
    {
        Db.ChangeTracker.Clear();
        return await Db.Customers.IgnoreQueryFilters().Where(c => c.CompanyId == CompanyId).ToListAsync();
    }

    public async Task<List<CustomerStatusHistory>> HistoryAsync(Guid customerId)
    {
        Db.ChangeTracker.Clear();
        return await Db.CustomerStatusHistory.IgnoreQueryFilters()
            .Where(h => h.CustomerId == customerId)
            .OrderBy(h => h.OccurredAt)
            .ToListAsync();
    }

    public async Task<List<CustomerStage>> StagesAsync()
    {
        Db.ChangeTracker.Clear();
        return await Db.CustomerStages.IgnoreQueryFilters().Where(s => s.CompanyId == CompanyId).ToListAsync();
    }

    public async Task DeactivatePersonAsync(Guid personId)
    {
        var person = await Db.Persons.IgnoreQueryFilters().SingleAsync(p => p.Id == personId);
        person.Deactivate();
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    private async Task SeedAsync()
    {
        Db.Companies.Add(_company);
        Db.ApplicationUsers.Add(new ApplicationUser { Id = UserId, FirstName = "Ana", LastName = "User", Email = "ana@t.local", UserName = "ana@t.local", IsActive = true });

        var idType = new IdentificationType { Name = "Cedula" };
        Db.IdentificationTypes.Add(idType);

        var person = new Person { CompanyId = CompanyId, PersonType = PersonType.Physical, FirstName = "Livingstone", LastName = "Cano", IdentificationTypeId = idType.Id, IdentificationNumber = "001" };
        var other = new Person { CompanyId = CompanyId, PersonType = PersonType.Legal, FirstName = "Norma", LastName = "Osorio", CommercialName = "Osorio SRL", IdentificationTypeId = idType.Id, IdentificationNumber = "002" };
        PersonId = person.Id;
        OtherPersonId = other.Id;
        Db.Persons.AddRange(person, other);

        foreach (var (code, name, order, isDefault) in new[]
                 {
                     (CustomerStage.Codes.Customer, "Cliente", 1, true),
                     (CustomerStage.Codes.Recurring, "Recurrente", 2, false),
                     (CustomerStage.Codes.Former, "Cliente anterior", 3, false)
                 })
        {
            var stage = CustomerStage.Create(CompanyId, code, name, null, order, isDefault);
            Stage[code] = stage.Id;
            Db.CustomerStages.Add(stage);
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
