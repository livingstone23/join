using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Customers;
using JOIN.Application.Interface.Email;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Customers;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;
using JOIN.Infrastructure.Email;
using JOIN.Persistence.Contexts;
using JOIN.Persistence.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.EmailTemplates;

/// <summary>
/// Test bed of the email templates (SPEC 71): InMemory <see cref="ApplicationDbContext"/>, the real
/// <see cref="UnitOfWork"/> and the Application services (renderer included). The lead scope resolver is
/// mocked so the render handler can be exercised without the sales teams, and the Dapper connection is
/// the fake of <c>Common.TestDoubles</c> so the query handlers can be fed with result sets.
/// </summary>
internal sealed class EmailTemplateTestContext : IDisposable
{
    private readonly Company _company = new() { Name = "Acme", TaxId = "TPL-1", IsActive = true };

    public Guid CompanyId => _company.Id;
    public Mock<ICurrentUserService> CurrentUser { get; } = new();
    public ApplicationDbContext Db { get; }
    public ServiceProvider Services { get; }
    public FakeSqlConnectionFactory Sql { get; } = new();

    public Guid ActingCompanyId { get; set; }
    public bool IsSuperAdmin { get; set; }
    public Guid CurrentUserId { get; set; }

    private EmailTemplateTestContext()
    {
        CurrentUser.SetupGet(x => x.CompanyId).Returns(() => ActingCompanyId);
        CurrentUser.SetupGet(x => x.UserId).Returns(() => CurrentUserId.ToString());
        CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        CurrentUser.Setup(x => x.IsInRole("SuperAdmin")).Returns(() => IsSuperAdmin);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"email-template-tests-{Guid.NewGuid()}")
            .Options;
        Db = new ApplicationDbContext(options, new AuditableEntitySaveChangesInterceptor(CurrentUser.Object), CurrentUser.Object);

        var unitOfWork = new LockFreeUnitOfWork(new UnitOfWork(Db, new DapperContext(new ConfigurationBuilder().Build()), Sql));

        var scopeResolver = new Mock<ILeadAccessScopeResolver>();
        scopeResolver.Setup(x => x.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid companyId, Guid userId, CancellationToken _) =>
                new LeadAccessScope(userId, true, [], [], true));

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
        services.AddSingleton<ISqlConnectionFactory>(Sql);
        services.AddSingleton<ILeadAccessScopeResolver>(scopeResolver.Object);
        services.AddSingleton<IHtmlSanitizer, HtmlSanitizer>();
        Services = services.BuildServiceProvider();
    }

    public static async Task<EmailTemplateTestContext> CreateAsync()
    {
        var context = new EmailTemplateTestContext();
        context.ActingCompanyId = context.CompanyId;

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "remitente@acme.test",
            Email = "remitente@acme.test",
            FirstName = "Remitente",
            LastName = "Acme"
        };
        context.CurrentUserId = user.Id;

        context.Db.Companies.Add(context._company);
        context.Db.ApplicationUsers.Add(user);
        await context.Db.SaveChangesAsync();
        context.Db.ChangeTracker.Clear();
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

    /// <summary>Adds a live type of the company and returns its id.</summary>
    public async Task<Guid> AddTypeAsync(string code = "INTRO", string name = "Presentación", bool isActive = true)
    {
        var type = new EmailTemplateType
        {
            CompanyId = CompanyId,
            Code = code,
            Name = name,
            SortOrder = 1,
            IsActive = isActive
        };
        Db.EmailTemplateTypes.Add(type);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return type.Id;
    }

    /// <summary>Adds a live template of the company and returns its id.</summary>
    public async Task<Guid> AddTemplateAsync(
        Guid typeId,
        string name = "Bienvenida",
        string subject = "Hola {{Persona.Nombre}}",
        string bodyHtml = "<p>Hola {{Persona.Nombre}}</p>",
        bool isActive = true)
    {
        var template = new EmailTemplate
        {
            CompanyId = CompanyId,
            EmailTemplateTypeId = typeId,
            Name = name,
            Subject = subject,
            BodyHtml = bodyHtml,
            IsActive = isActive
        };
        Db.EmailTemplates.Add(template);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return template.Id;
    }

    /// <summary>Adds a person of the company and returns it.</summary>
    public async Task<Person> AddPersonAsync(string firstName = "Ana", string lastName = "López")
    {
        var person = new Person
        {
            CompanyId = CompanyId,
            FirstName = firstName,
            LastName = lastName
        };
        Db.Persons.Add(person);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return person;
    }

    /// <summary>Adds an active customer of the person and returns it.</summary>
    public async Task<Customer> AddCustomerAsync(Guid personId, string code = "C-001")
    {
        var customer = Customer.Create(CompanyId, personId, CurrentUserId, code, Guid.NewGuid());
        Db.Customers.Add(customer);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return customer;
    }

    /// <summary>Adds a lead of the person and returns it.</summary>
    public async Task<Lead> AddLeadAsync(Guid personId, string code = "LD-000001", string topic = "Renovación")
    {
        var lead = new Lead
        {
            CompanyId = CompanyId,
            PersonId = personId,
            Code = code,
            Topic = topic,
            OwnerUserId = CurrentUserId,
            Status = LeadStatus.Open
        };
        Db.Leads.Add(lead);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return lead;
    }

    /// <summary>Adds an active e-mail contact of the person.</summary>
    public async Task<PersonContact> AddEmailContactAsync(
        Guid personId,
        string value = "ana@acme.test",
        bool isPrimary = true,
        JOIN.Domain.Enums.ContactType contactType = JOIN.Domain.Enums.ContactType.PrimaryEmail)
    {
        var contact = PersonContact.Create(CompanyId, personId, contactType, value);
        if (isPrimary)
        {
            contact.SetAsPrimary();
        }
        Db.PersonContacts.Add(contact);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return contact;
    }

    public void Dispose()
    {
        Services.Dispose();
        Db.Dispose();
    }
}
