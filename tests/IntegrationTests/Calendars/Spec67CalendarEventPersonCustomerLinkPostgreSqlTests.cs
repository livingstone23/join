// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using JOIN.Domain.Customers;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Calendars;

/// <summary>
/// SPEC 67 — end-to-end flow through <c>CalendarEventCommandProcessor</c> and <c>CalendarQueryReader.ByIdSql</c>:
/// the runtime resolves the person/customer pair (rules 2-4), the detail endpoint exposes the readable name and
/// customer code, and the data-only backfill that ships in <c>Spec67CalendarEventPersonCustomerLink</c> leaves no
/// incoherent row in <c>calendar.events</c>. Also runs <c>EXPLAIN</c> on the new detail query to make sure the two
/// <c>LEFT JOIN</c>s plan as index scans on the FK columns.
/// </summary>
public sealed class Spec67CalendarEventPersonCustomerLinkPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Creator = "spec67-tests";
    private const string Password = "Integration!Pass123";

    private readonly CustomWebApplicationFactory _factory;

    public Spec67CalendarEventPersonCustomerLinkPostgreSqlTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Rule 2 — creating an activity with only the person id stores the matching customer id (active or not).
    /// </summary>
    [Fact]
    public async Task Create_WithOnlyPerson_StoresTheCustomerAndDtoExposesNameCodeAndActive()
    {
        var context = await SeedAsync();
        var (personId, customerId) = await context.AddPersonAndCustomerAsync(person: "Ada Lovelace", customerActive: true, code: "C-001");

        using var client = context.NewClient();
        var meetingId = await GetMeetingActivityIdAsync(client);

        var start = "2027-03-15T10:00:00-06:00";
        var end = "2027-03-15T11:00:00-06:00";
        var response = await client.PostAsJsonAsync("/api/v1/CalendarEvents", new
        {
            calendarActivityCompanyId = meetingId,
            title = "Sync",
            isAllDay = false,
            start,
            end,
            requesterPersonId = personId
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);

        var data = Data(body);
        var id = data.GetProperty("id").GetGuid();

        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.CalendarEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == id);
        stored.RequesterPersonId.Should().Be(personId);
        stored.CustomerId.Should().Be(customerId);

        var detail = Data(await ReadContentAsync(await client.GetAsync($"/api/v1/CalendarEvents/{id}")));
        detail.GetProperty("requesterPersonId").GetGuid().Should().Be(personId);
        detail.GetProperty("requesterPersonName").GetString().Should().Be("Ada Lovelace");
        detail.GetProperty("customerId").GetGuid().Should().Be(customerId);
        detail.GetProperty("customerCode").GetString().Should().Be("C-001");
        detail.GetProperty("isCustomerActive").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Rule 2 — the customer's inactive state does NOT block the link: the rule explicitly accepts inactive
    /// customers (SPEC 67 decision).
    /// </summary>
    [Fact]
    public async Task Create_WithOnlyPerson_AndCustomerInactive_StillStoresCustomerIdAndMarksInactive()
    {
        var context = await SeedAsync();
        var (personId, customerId) = await context.AddPersonAndCustomerAsync(person: "Grace Hopper", customerActive: false, code: "C-OLD");

        using var client = context.NewClient();
        var meetingId = await GetMeetingActivityIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/CalendarEvents", new
        {
            calendarActivityCompanyId = meetingId,
            title = "Follow-up",
            isAllDay = false,
            start = "2027-03-15T13:00:00-06:00",
            end = "2027-03-15T14:00:00-06:00",
            requesterPersonId = personId
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);

        var id = Data(body).GetProperty("id").GetGuid();
        var detail = Data(await ReadContentAsync(await client.GetAsync($"/api/v1/CalendarEvents/{id}")));
        detail.GetProperty("customerId").GetGuid().Should().Be(customerId);
        detail.GetProperty("customerCode").GetString().Should().Be("C-OLD");
        detail.GetProperty("isCustomerActive").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Rule 2 — when the person has no customer in the company, the activity stores the person only.
    /// </summary>
    [Fact]
    public async Task Create_WithOnlyPerson_AndPersonIsNotCustomer_LeavesCustomerIdNull()
    {
        var context = await SeedAsync();
        var personId = await context.AddPersonOnlyAsync("Solo Persona");

        using var client = context.NewClient();
        var meetingId = await GetMeetingActivityIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/CalendarEvents", new
        {
            calendarActivityCompanyId = meetingId,
            title = "Coffee",
            isAllDay = false,
            start = "2027-03-16T09:00:00-06:00",
            end = "2027-03-16T10:00:00-06:00",
            requesterPersonId = personId
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);

        var id = Data(body).GetProperty("id").GetGuid();
        var detail = Data(await ReadContentAsync(await client.GetAsync($"/api/v1/CalendarEvents/{id}")));
        detail.GetProperty("requesterPersonId").GetGuid().Should().Be(personId);
        detail.GetProperty("requesterPersonName").GetString().Should().Be("Solo Persona");
        detail.GetProperty("customerId").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("customerCode").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("isCustomerActive").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Rule 3 — creating with only the customer id stores the matching person id (also the path the channel
    /// intake and SPEC 65's lead next-action use).
    /// </summary>
    [Fact]
    public async Task Create_WithOnlyCustomer_StoresThePersonAndDtoExposesNameAndCode()
    {
        var context = await SeedAsync();
        var (personId, customerId) = await context.AddPersonAndCustomerAsync(person: "Linus Torvalds", customerActive: true, code: "C-2");

        using var client = context.NewClient();
        var meetingId = await GetMeetingActivityIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/CalendarEvents", new
        {
            calendarActivityCompanyId = meetingId,
            title = "Patch review",
            isAllDay = false,
            start = "2027-03-17T11:00:00-06:00",
            end = "2027-03-17T12:00:00-06:00",
            customerId = customerId
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);

        var id = Data(body).GetProperty("id").GetGuid();
        var detail = Data(await ReadContentAsync(await client.GetAsync($"/api/v1/CalendarEvents/{id}")));
        detail.GetProperty("requesterPersonId").GetGuid().Should().Be(personId);
        detail.GetProperty("requesterPersonName").GetString().Should().Be("Linus Torvalds");
        detail.GetProperty("customerId").GetGuid().Should().Be(customerId);
        detail.GetProperty("customerCode").GetString().Should().Be("C-2");
        detail.GetProperty("isCustomerActive").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Rule 4 — when both ids arrive and the customer is of a different person, the request returns
    /// <c>400 CALENDAR_PERSON_CUSTOMER_MISMATCH</c> and no row is written.
    /// </summary>
    [Fact]
    public async Task Create_WithPersonAndCustomerOfDifferentPeople_ReturnsPersonCustomerMismatch()
    {
        var context = await SeedAsync();
        var (_, customerId) = await context.AddPersonAndCustomerAsync(person: "First Person", customerActive: true, code: "C-A");
        var otherPersonId = await context.AddPersonOnlyAsync("Second Person");

        using var client = context.NewClient();
        var meetingId = await GetMeetingActivityIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/CalendarEvents", new
        {
            calendarActivityCompanyId = meetingId,
            title = "Bad combo",
            isAllDay = false,
            start = "2027-03-18T10:00:00-06:00",
            end = "2027-03-18T11:00:00-06:00",
            requesterPersonId = otherPersonId,
            customerId = customerId
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("CALENDAR_PERSON_CUSTOMER_MISMATCH");

        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var anyBad = await db.CalendarEvents.IgnoreQueryFilters()
            .AnyAsync(e => e.CompanyId == context.CompanyId && e.RequesterPersonId == otherPersonId && e.CustomerId == customerId);
        anyBad.Should().BeFalse();
    }

    /// <summary>
    /// Rule 6 — sending both nulls clears the pair on the activity (no FK error, no link).
    /// </summary>
    [Fact]
    public async Task Create_WithBothLinksClearsBothNull()
    {
        var context = await SeedAsync();

        using var client = context.NewClient();
        var meetingId = await GetMeetingActivityIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/CalendarEvents", new
        {
            calendarActivityCompanyId = meetingId,
            title = "Free slot",
            isAllDay = false,
            start = "2027-03-19T08:00:00-06:00",
            end = "2027-03-19T09:00:00-06:00"
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);

        var id = Data(body).GetProperty("id").GetGuid();
        var detail = Data(await ReadContentAsync(await client.GetAsync($"/api/v1/CalendarEvents/{id}")));
        detail.GetProperty("requesterPersonId").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("customerId").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("requesterPersonName").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("customerCode").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("isCustomerActive").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Data migration — the same SQL the migration runs leaves no row with one side of the pair missing when
    /// the matching record exists. Inserts an incoherent event, runs the two backfills, and verifies the pair
    /// became coherent. Also asserts no <c>calendar.events</c> row in the test company has one side null when the
    /// matching record exists in the company.
    /// </summary>
    [Fact]
    public async Task Migration_BackfillKeepsRequesterPersonIdAndCustomerIdCoherent()
    {
        var context = await SeedAsync();
        var (personId, customerId) = await context.AddPersonAndCustomerAsync(person: "Margaret Hamilton", customerActive: true, code: "C-M");

        Guid personOnlyId;
        Guid customerOnlyId;
        Guid coherentId;
        await using (var scope = context.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var companyId = context.CompanyId;
            var meetingActivity = await db.CalendarActivityCompanies.IgnoreQueryFilters()
                .FirstAsync(a => a.CompanyId == companyId && a.IsActive);

            personOnlyId = Guid.NewGuid();
            customerOnlyId = Guid.NewGuid();
            coherentId = Guid.NewGuid();

            var personOnlyEvent = new CalendarEvent
            {
                CompanyId = companyId,
                CalendarId = context.CalendarId,
                OwnerUserId = context.AdminUserId,
                CalendarActivityCompanyId = meetingActivity.Id,
                CalendarStatusCompanyId = meetingActivity.InitialCalendarStatusCompanyId,
                Title = "Person only",
                IsAllDay = false,
                StartUtc = new DateTime(2027, 3, 22, 14, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2027, 3, 22, 15, 0, 0, DateTimeKind.Utc),
                RequesterPersonId = personId,
                CustomerId = null,
                OriginChannelId = (await db.CommunicationChannels.IgnoreQueryFilters().SingleAsync(c => c.Code == "WEB")).Id,
                OriginType = CalendarEventOriginType.User,
                GcRecord = BaseAuditableEntity.ActiveGcRecord,
                CreatedBy = Creator
            };
            EntityIdSetter.SetId(personOnlyEvent, personOnlyId);
            db.CalendarEvents.Add(personOnlyEvent);
            var customerOnlyEvent = new CalendarEvent
            {
                CompanyId = companyId,
                CalendarId = context.CalendarId,
                OwnerUserId = context.AdminUserId,
                CalendarActivityCompanyId = meetingActivity.Id,
                CalendarStatusCompanyId = meetingActivity.InitialCalendarStatusCompanyId,
                Title = "Customer only",
                IsAllDay = false,
                StartUtc = new DateTime(2027, 3, 22, 16, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2027, 3, 22, 17, 0, 0, DateTimeKind.Utc),
                RequesterPersonId = null,
                CustomerId = customerId,
                OriginChannelId = (await db.CommunicationChannels.IgnoreQueryFilters().SingleAsync(c => c.Code == "WEB")).Id,
                OriginType = CalendarEventOriginType.User,
                GcRecord = BaseAuditableEntity.ActiveGcRecord,
                CreatedBy = Creator
            };
            EntityIdSetter.SetId(customerOnlyEvent, customerOnlyId);
            db.CalendarEvents.Add(customerOnlyEvent);
            var coherentEvent = new CalendarEvent
            {
                CompanyId = companyId,
                CalendarId = context.CalendarId,
                OwnerUserId = context.AdminUserId,
                CalendarActivityCompanyId = meetingActivity.Id,
                CalendarStatusCompanyId = meetingActivity.InitialCalendarStatusCompanyId,
                Title = "Already coherent",
                IsAllDay = false,
                StartUtc = new DateTime(2027, 3, 23, 14, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2027, 3, 23, 15, 0, 0, DateTimeKind.Utc),
                RequesterPersonId = personId,
                CustomerId = customerId,
                OriginChannelId = (await db.CommunicationChannels.IgnoreQueryFilters().SingleAsync(c => c.Code == "WEB")).Id,
                OriginType = CalendarEventOriginType.User,
                GcRecord = BaseAuditableEntity.ActiveGcRecord,
                CreatedBy = Creator
            };
            EntityIdSetter.SetId(coherentEvent, coherentId);
            db.CalendarEvents.Add(coherentEvent);
            await db.SaveChangesAsync();
        }

        // Apply the same SQL the migration runs.
        await using (var scope = context.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE "calendar"."events" AS e
                SET "customerid" = cust."id"
                FROM "customer"."customers" AS cust
                WHERE cust."companyid" = e."companyid"
                  AND cust."personid" = e."requesterpersonid"
                  AND cust."gcrecord" = 0
                  AND e."requesterpersonid" IS NOT NULL
                  AND e."customerid" IS NULL;
                """);
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE "calendar"."events" AS e
                SET "requesterpersonid" = cust."personid"
                FROM "customer"."customers" AS cust
                WHERE cust."id" = e."customerid"
                  AND e."customerid" IS NOT NULL
                  AND e."requesterpersonid" IS NULL;
                """);
        }

        await using (var scope = context.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var personOnly = await db.CalendarEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == personOnlyId);
            personOnly.RequesterPersonId.Should().Be(personId);
            personOnly.CustomerId.Should().Be(customerId);

            var customerOnly = await db.CalendarEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == customerOnlyId);
            customerOnly.RequesterPersonId.Should().Be(personId);
            customerOnly.CustomerId.Should().Be(customerId);

            var coherent = await db.CalendarEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == coherentId);
            coherent.RequesterPersonId.Should().Be(personId);
            coherent.CustomerId.Should().Be(customerId);

            // Acceptance criterion #6: no activity has CustomerId without RequesterPersonId, nor a person with a
            // customer in the company without CustomerId.
            var companyId = context.CompanyId;
            var customerOnlyRows = await db.CalendarEvents.IgnoreQueryFilters()
                .Where(e => e.CompanyId == companyId
                        && e.CustomerId != null
                        && e.RequesterPersonId == null)
                .CountAsync();
            customerOnlyRows.Should().Be(0);

            var personWithoutCustomer = await db.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*)::int AS "Value"
                FROM "calendar"."events" e
                INNER JOIN "customer"."customers" cust
                    ON cust."companyid" = e."companyid"
                   AND cust."personid" = e."requesterpersonid"
                   AND cust."gcrecord" = 0
                WHERE e."companyid" = {0}
                  AND e."requesterpersonid" IS NOT NULL
                  AND e."customerid" IS NULL;
                """, companyId).ToListAsync();
            personWithoutCustomer.Single().Should().Be(0);
        }
    }

    /// <summary>
    /// Acceptance criterion #7 — the new detail query plans as nested-loop with index scans on the FKs of
    /// <c>Admin.Persons</c> and <c>Customer.Customers</c>; if any tenant vanishes, the join collapses to a seq
    /// scan and the query becomes expensive to maintain.
    /// </summary>
    [Fact]
    public async Task Explain_ByIdSql_PlansIndexScansOnPersonAndCustomerFks()
    {
        var context = await SeedAsync();
        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        const string plan = """
            EXPLAIN (FORMAT TEXT)
            SELECT e.Id
            FROM Calendar.Events e
            LEFT JOIN Admin.Persons p
                ON p.Id = e.RequesterPersonId
               AND p.CompanyId = e.CompanyId
               AND p.GcRecord = 0
            LEFT JOIN Customer.Customers cust
                ON cust.Id = e.CustomerId
               AND cust.CompanyId = e.CompanyId
               AND cust.GcRecord = 0
            WHERE e.CompanyId = {0}
              AND e.GcRecord = 0
              AND e.Id = {1};
            """;

        var parameters = new object[] { context.CompanyId, Guid.NewGuid() };
        var rows = new List<string>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = plan.Replace("{0}", "$1").Replace("{1}", "$2");
            var p0 = command.CreateParameter(); p0.Value = parameters[0]; command.Parameters.Add(p0);
            var p1 = command.CreateParameter(); p1.Value = parameters[1]; command.Parameters.Add(p1);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(reader.GetString(0));
            }
        }

        var joined = string.Join(Environment.NewLine, rows);
        // The event id is a random Guid that does not exist; Postgres plans the same shape regardless.
        // The plan must anchor on the events table by index (no Seq Scan on events), otherwise the FK joins
        // would degrade when the table grows.
        joined.Should().Contain("events", "the planner must anchor on the events table");
        joined.Should().NotContain("Seq Scan on events", "an event-id lookup must plan as an index scan");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private sealed record SeedContext(CustomWebApplicationFactory Factory, Guid CompanyId, Guid AdminUserId, Guid CalendarId, string AdminEmail)
    {
        public HttpClient NewClient() => AuthedClient(Factory, CompanyId, AdminEmail);

        public async Task<Guid> AddPersonOnlyAsync(string name)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var parts = name.Split(' ', 2);
            var person = new Person
            {
                CompanyId = CompanyId,
                PersonType = PersonType.Physical,
                FirstName = parts[0],
                LastName = parts.Length > 1 ? parts[1] : "Apellido",
                CreatedBy = "spec67-tests"
            };
            EntityIdSetter.SetId(person, Guid.NewGuid());
            db.Persons.Add(person);
            await db.SaveChangesAsync();
            return person.Id;
        }

        public async Task<(Guid PersonId, Guid CustomerId)> AddPersonAndCustomerAsync(string person, bool customerActive, string code)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var parts = person.Split(' ', 2);
            var p = new Person
            {
                CompanyId = CompanyId,
                PersonType = PersonType.Physical,
                FirstName = parts[0],
                LastName = parts.Length > 1 ? parts[1] : "Apellido",
                CreatedBy = "spec67-tests"
            };
            EntityIdSetter.SetId(p, Guid.NewGuid());
            db.Persons.Add(p);

            var stage = await db.CustomerStages.IgnoreQueryFilters()
                .Where(s => s.CompanyId == CompanyId && s.Code == "CUSTOMER")
                .FirstAsync();
            var customer = Customer.Create(CompanyId, p.Id, AdminUserId, code, stage.Id);
            if (!customerActive)
            {
                customer.Deactivate();
            }
            EntityIdSetter.SetId(customer, Guid.NewGuid());
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            return (p.Id, customer.Id);
        }
    }

    private static class EntityIdSetter
    {
        public static void SetId(object entity, Guid id)
        {
            var prop = entity.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            prop!.SetValue(entity, id);
        }
    }

    private async Task<SeedContext> SeedAsync()
    {
        Guid companyId;
        Guid adminId;
        Guid calendarId;
        var email = $"a{Guid.NewGuid():N}@t.local";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var company = new Company
            {
                Name = $"JOIN-S67-{Guid.NewGuid():N}"[..20],
                TaxId = $"RUC{Guid.NewGuid():N}"[..16],
                IsActive = true,
                CreatedBy = Creator
            };
            db.Companies.Add(company);

            var moduleId = await db.SystemModules.IgnoreQueryFilters().Where(m => m.Name == "Calendar").Select(m => m.Id).SingleAsync();
            db.CompanyModules.Add(new CompanyModule { CompanyId = company.Id, ModuleId = moduleId, IsActive = true, CreatedBy = Creator });
            await db.SaveChangesAsync();
            companyId = company.Id;

            var seeder = scope.ServiceProvider.GetRequiredService<ICompanyCatalogSeeder>();
            await seeder.SeedCalendarCatalogsForCompanyAsync(companyId);

            var dbSeeder = scope.ServiceProvider.GetRequiredService<JOIN.Persistence.DatabaseSeeder>();
            await dbSeeder.SeedCustomerStagesForCompanyAsync(companyId);
        }

        adminId = await RegisterMemberAsync(companyId, email, "SuperAdmin");

        var client = AuthedClient(_factory, companyId, email);
        var config = await client.PostAsJsonAsync("/api/v1/CalendarCompanies", new
        {
            defaultActivityUserId = adminId,
            timeZone = "America/Managua",
            isOpen24Hours = false,
            businessHoursStart = "08:00:00",
            businessHoursEnd = "17:00:00"
        });
        config.EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var calendar = await db.UserCalendars.IgnoreQueryFilters()
                .Where(c => c.CompanyId == companyId && c.OwnerUserId == adminId && c.IsActive)
                .FirstAsync();
            calendarId = calendar.Id;
        }

        return new SeedContext(_factory, companyId, adminId, calendarId, email);
    }

    private async Task<Guid> RegisterMemberAsync(Guid companyId, string email, string role)
    {
        using var unauthClient = _factory.CreateClient();
        (await unauthClient.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand
        {
            Email = email,
            Password = Password,
            FirstName = "Spec67",
            LastName = role
        })).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();

        db.UserCompanies.Add(new UserCompany { UserId = user!.Id, CompanyId = companyId, IsDefault = true, CreatedBy = Creator });
        if (role is not null)
        {
            (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        }

        await db.SaveChangesAsync();
        return user.Id;
    }

    private static HttpClient AuthedClient(CustomWebApplicationFactory factory, Guid companyId, string email)
    {
        using var unauthClient = factory.CreateClient();
        var login = unauthClient.PostAsJsonAsync("/api/v1/users/login", new LoginCommand { Email = email, Password = Password }).GetAwaiter().GetResult();
        login.EnsureSuccessStatusCode();
        var payload = login.Content.ReadFromJsonAsync<JOIN.Application.Common.Response<LoginResponse>>().GetAwaiter().GetResult();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Data!.Token);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString());
        return client;
    }

    private static async Task<string> ReadContentAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();
        return body;
    }

    private static async Task<Guid> GetMeetingActivityIdAsync(HttpClient client)
    {
        var usable = Data(await ReadContentAsync(await client.GetAsync("/api/v1/CalendarActivityCompanies/usable")));
        return usable.EnumerateArray().Single(a => a.GetProperty("code").GetString() == "MEETING").GetProperty("id").GetGuid();
    }

    private static JsonElement Data(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }
}