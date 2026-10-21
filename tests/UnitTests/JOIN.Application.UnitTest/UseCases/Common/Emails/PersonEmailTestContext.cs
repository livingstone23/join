using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Customers;
using JOIN.Application.Interface.Email;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Domain.Admin;
using JOIN.Domain.Catalog;
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
using Moq;
using LeadContactType = JOIN.Domain.Customers.Leads.ContactType;
using PersonContactType = JOIN.Domain.Enums.ContactType;

namespace JOIN.Application.UnitTest.UseCases.Common.Emails;

/// <summary>
/// Test bed of the sending of emails (SPEC 72): InMemory <see cref="ApplicationDbContext"/>, the real
/// <see cref="UnitOfWork"/> and Application services, a capturing <see cref="IEmailService"/>, an in-memory storage
/// provider and the Dapper fake for the template context (SPEC 71) and the queries. The lead scope sees every lead.
/// </summary>
internal sealed class PersonEmailTestContext : IDisposable
{
    private readonly Company _company = new() { Name = "Acme", TaxId = "MAIL-1", IsActive = true };

    public Guid CompanyId => _company.Id;
    public Mock<ICurrentUserService> CurrentUser { get; } = new();
    public ApplicationDbContext Db { get; }
    public ServiceProvider Services { get; }
    public FakeSqlConnectionFactory Sql { get; } = new();
    public CapturingEmailService Email { get; } = new();
    public FakeSenderDefaults SenderDefaults { get; } = new();
    public Mock<IFileStorageService> Storage { get; } = new();
    public Mock<IFileStorageResolver> Resolver { get; } = new();
    public Dictionary<string, byte[]> Files { get; } = [];

    public Guid ActingCompanyId { get; set; }
    public Guid CurrentUserId { get; set; }
    public Guid EmailContactTypeCompanyId { get; private set; }

    private PersonEmailTestContext()
    {
        CurrentUser.SetupGet(x => x.CompanyId).Returns(() => ActingCompanyId);
        CurrentUser.SetupGet(x => x.UserId).Returns(() => CurrentUserId.ToString());
        CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"person-email-tests-{Guid.NewGuid()}")
            .Options;
        Db = new ApplicationDbContext(options, new AuditableEntitySaveChangesInterceptor(CurrentUser.Object), CurrentUser.Object);
        var unitOfWork = new LockFreeUnitOfWork(new UnitOfWork(Db, new DapperContext(new ConfigurationBuilder().Build()), Sql));

        var scopeResolver = new Mock<ILeadAccessScopeResolver>();
        scopeResolver.Setup(x => x.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid userId, CancellationToken _) => new LeadAccessScope(userId, true, [], [], true));

        Storage.SetupGet(x => x.Kind).Returns(StorageProviderKind.Local);
        Storage.Setup(x => x.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => Files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);
        Resolver.Setup(x => x.Resolve(StorageProviderKind.Local)).Returns(Storage.Object);
        Resolver.Setup(x => x.Resolve(StorageProviderKind.AzureBlob)).Returns((IFileStorageService?)null);

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
        services.AddSingleton<IEmailService>(Email);
        services.AddSingleton<IEmailSenderDefaults>(SenderDefaults);
        services.AddSingleton(Resolver.Object);
        Services = services.BuildServiceProvider();
    }

    public static async Task<PersonEmailTestContext> CreateAsync(bool withSettings = true, bool withEmailContactType = true)
    {
        var context = new PersonEmailTestContext();
        context.ActingCompanyId = context.CompanyId;

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "vendedor@acme.test",
            Email = "vendedor@acme.test",
            FirstName = "Vera",
            LastName = "Vendedora"
        };
        context.CurrentUserId = user.Id;
        context.Db.Companies.Add(context._company);
        context.Db.ApplicationUsers.Add(user);

        if (withSettings)
        {
            context.Db.EmailCompanySettings.Add(new EmailCompanySettings
            {
                CompanyId = context.CompanyId,
                FromEmail = "ventas@acme.test",
                FromName = "Ventas Acme"
            });
        }

        var master = new LeadContactType { Code = "EMAIL", Name = "Correo", Icon = "mail", IsActive = true };
        context.Db.ContactTypes.Add(master);
        var copy = new ContactTypeCompany { CompanyId = context.CompanyId, ContactTypeId = master.Id, Name = "Correo", IsActive = withEmailContactType };
        context.EmailContactTypeCompanyId = copy.Id;
        context.Db.ContactTypeCompanies.Add(copy);

        await context.Db.SaveChangesAsync();
        context.Db.ChangeTracker.Clear();
        return context;
    }

    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(request.GetType(), typeof(TResponse));
        dynamic handler = Services.GetRequiredService(handlerType);
        return (Task<TResponse>)handler.Handle((dynamic)request, CancellationToken.None);
    }

    /// <summary>A person of the company with an e-mail contact (primary by default).</summary>
    public async Task<Person> AddPersonAsync(string email = "ana@cliente.test", bool active = true, PersonType type = PersonType.Physical)
    {
        var person = new Person { CompanyId = CompanyId, FirstName = "Ana", LastName = "López", PersonType = type };
        if (!active)
        {
            person.Deactivate();
        }

        Db.Persons.Add(person);
        if (!string.IsNullOrEmpty(email))
        {
            var contact = PersonContact.Create(CompanyId, person.Id, PersonContactType.PrimaryEmail, email);
            contact.SetAsPrimary();
            Db.PersonContacts.Add(contact);
        }

        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return person;
    }

    public async Task AddContactAsync(Guid personId, string value, PersonContactType type = PersonContactType.AlternativeEmail)
    {
        Db.PersonContacts.Add(PersonContact.Create(CompanyId, personId, type, value));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public async Task<Lead> AddLeadAsync(Guid personId, LeadStatus status = LeadStatus.Open)
    {
        var lead = new Lead
        {
            CompanyId = CompanyId,
            PersonId = personId,
            Code = $"LD-{Random.Shared.Next(100000, 999999)}",
            Topic = "Renovación",
            OwnerUserId = CurrentUserId,
            Status = status
        };
        Db.Leads.Add(lead);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return lead;
    }

    public async Task<Customer> AddCustomerAsync(Guid personId)
    {
        var customer = Customer.Create(CompanyId, personId, CurrentUserId, "C-001", Guid.NewGuid());
        Db.Customers.Add(customer);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return customer;
    }

    public async Task<ProductDocument> AddProductDocumentAsync(byte[] content, StorageProviderKind provider = StorageProviderKind.Local, Guid? companyId = null)
    {
        var document = new ProductDocument
        {
            CompanyId = companyId ?? CompanyId,
            ProductId = Guid.NewGuid(),
            ProductDocumentTypeId = Guid.NewGuid(),
            DisplayName = "Ficha",
            OriginalName = "ficha.pdf",
            NewName = "x.pdf",
            Path = $"products/{Guid.NewGuid():N}.pdf",
            StorageProvider = provider,
            ContentType = "application/pdf",
            SizeBytes = content.LongLength
        };
        Files[document.Path] = content;
        Db.ProductDocuments.Add(document);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return document;
    }

    public async Task<LeadDocument> AddLeadDocumentAsync(Guid leadId, byte[] content)
    {
        var document = new LeadDocument
        {
            CompanyId = CompanyId,
            LeadId = leadId,
            OriginalName = "propuesta.pdf",
            NewName = "y.pdf",
            Path = $"leads/{Guid.NewGuid():N}.pdf",
            StorageProvider = StorageProviderKind.Local,
            ContentType = "application/pdf",
            SizeBytes = content.LongLength,
            UploadedByUserId = CurrentUserId
        };
        Files[document.Path] = content;
        Db.LeadDocuments.Add(document);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return document;
    }

    /// <summary>The result sets the template context reader (SPEC 71) reads: the person row and, with a lead, its scope and row.</summary>
    public void SetContext(bool withLead = false, string firstName = "Ana")
    {
        var sets = new List<FakeResultSet>
        {
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["PersonType"] = (int)PersonType.Physical, ["FirstName"] = firstName, ["MiddleName"] = null, ["LastName"] = "López",
                ["SecondLastName"] = null, ["CommercialName"] = null, ["CustomerCode"] = null, ["Email"] = "ana@cliente.test",
                ["CompanyName"] = "Acme", ["TimeZone"] = null, ["UserName"] = "Vera Vendedora", ["UserEmail"] = "vendedor@acme.test"
            })
        };
        if (withLead)
        {
            sets.Add(FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Status"] = (int)LeadStatus.Open, ["OwnerUserId"] = CurrentUserId, ["SalesTeamId"] = null
            }));
            sets.Add(FakeResultSet.FromRows(new Dictionary<string, object?> { ["Code"] = "LD-1", ["Topic"] = "Renovación" }));
        }

        Sql.SetResults([.. sets]);
    }

    public async Task<List<PersonEmailMessage>> MessagesAsync()
    {
        Db.ChangeTracker.Clear();
        return await Db.PersonEmailMessages.IgnoreQueryFilters().ToListAsync();
    }

    public void Dispose()
    {
        Services.Dispose();
        Db.Dispose();
    }

    /// <summary>Captures every message; <see cref="Fail"/> makes the provider reject them.</summary>
    internal sealed class CapturingEmailService : IEmailService
    {
        public List<EmailMessage> Messages { get; } = [];
        public bool Fail { get; set; }

        public Task<bool> SendEmailAsync(string to, string subject, string htmlContent) => Task.FromResult(true);

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.FromResult(Fail ? EmailSendResult.Fail("550 sender not verified") : EmailSendResult.Ok("sg-123"));
        }
    }

    internal sealed class FakeSenderDefaults : IEmailSenderDefaults
    {
        public string? FromEmail { get; set; } = "no-reply@join.test";
        public string? FromName { get; set; } = "JOIN";
    }
}
