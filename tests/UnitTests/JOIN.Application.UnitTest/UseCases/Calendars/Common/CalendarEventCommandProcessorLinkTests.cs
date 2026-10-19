using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.CreateCalendarEvent;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Customers;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.Common;

/// <summary>
/// SPEC 67 — person/customer link resolution in <see cref="CalendarEventCommandProcessor"/>. The processor keeps
/// the two columns consistent on every write: rule 2 fills the customer from the person, rule 3 fills the person
/// from the customer, rule 4 rejects a mismatch with <c>CALENDAR_PERSON_CUSTOMER_MISMATCH</c>, and rule 6 clears
/// the pair when both arrive null. All write paths (<c>CalendarEvents</c>, <c>CalendarPanel</c>,
/// <c>CalendarChannelIntake</c>, <c>LeadNextActionScheduler</c>) go through the processor, so testing it covers them
/// all.
/// </summary>
public sealed class CalendarEventCommandProcessorLinkTests
{
    private static readonly Fixture _fixture = new();
    private const string TimeZoneId = "America/Managua";

    /// <summary>
    /// Rule 2 — only the person arrives and the person has a customer in the company: the entity stores both ids
    /// and the inactive customer is also linked (the rule explicitly accepts inactive customers).
    /// </summary>
    [Fact]
    public async Task Handle_WhenOnlyPersonGiven_AndPersonHasInactiveCustomer_FillsCustomerId()
    {
        var context = new LinkTestContext();
        var customer = context.AddCustomer(personId: context.PersonId, active: false, code: "C-INACT");

        var response = await context.CreateHandler().Handle(context.BuildCommand(
            requesterPersonId: context.PersonId,
            customerId: null), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.InsertedEvent!.RequesterPersonId.Should().Be(context.PersonId);
        context.InsertedEvent.CustomerId.Should().Be(customer.Id);
    }

    /// <summary>
    /// Rule 2 — only the person arrives and the person is NOT a customer: the entity stores the person but no
    /// customer.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOnlyPersonGiven_AndPersonIsNotCustomer_LeavesCustomerIdNull()
    {
        var context = new LinkTestContext();
        // No customer for the person.
        context.CustomerRepositoryMock
            .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Customer, bool>>>()))
            .ReturnsAsync(Array.Empty<Customer>());

        var response = await context.CreateHandler().Handle(context.BuildCommand(
            requesterPersonId: context.PersonId,
            customerId: null), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.InsertedEvent!.RequesterPersonId.Should().Be(context.PersonId);
        context.InsertedEvent.CustomerId.Should().BeNull();
    }

    /// <summary>
    /// Rule 3 — only the customer arrives: the entity stores both ids and the person comes from the customer.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOnlyCustomerGiven_FillsPersonFromCustomer()
    {
        var context = new LinkTestContext();
        var customer = context.AddCustomer(personId: context.PersonId, active: true, code: "C-1");

        var response = await context.CreateHandler().Handle(context.BuildCommand(
            requesterPersonId: null,
            customerId: customer.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.InsertedEvent!.RequesterPersonId.Should().Be(context.PersonId);
        context.InsertedEvent.CustomerId.Should().Be(customer.Id);
    }

    /// <summary>
    /// Rule 4 — both arrive and the customer is of a different person: the processor rejects the request with
    /// <c>CALENDAR_PERSON_CUSTOMER_MISMATCH</c> and does not insert a calendar event.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPersonAndCustomerMismatch_ReturnsPersonCustomerMismatch()
    {
        var context = new LinkTestContext();
        var otherPersonId = Guid.NewGuid();
        context.AddPerson(otherPersonId: otherPersonId);
        var customer = context.AddCustomer(personId: otherPersonId, active: true, code: "C-OTHER");

        var response = await context.CreateHandler().Handle(context.BuildCommand(
            requesterPersonId: context.PersonId,
            customerId: customer.Id), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.PersonCustomerMismatch);
        context.CalendarEventsRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    /// <summary>
    /// Rule 4 — both arrive and the customer belongs to that person: the pair is accepted as coherent.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPersonAndCustomerAgree_AcceptsPair()
    {
        var context = new LinkTestContext();
        var customer = context.AddCustomer(personId: context.PersonId, active: true, code: "C-OK");

        var response = await context.CreateHandler().Handle(context.BuildCommand(
            requesterPersonId: context.PersonId,
            customerId: customer.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.InsertedEvent!.RequesterPersonId.Should().Be(context.PersonId);
        context.InsertedEvent.CustomerId.Should().Be(customer.Id);
    }

    /// <summary>
    /// Rule 6 — both nulls clear the pair: the entity stores nulls.
    /// </summary>
    [Fact]
    public async Task Handle_WhenBothNull_ClearsBothLinks()
    {
        var context = new LinkTestContext();

        var response = await context.CreateHandler().Handle(context.BuildCommand(
            requesterPersonId: null,
            customerId: null), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.InsertedEvent!.RequesterPersonId.Should().BeNull();
        context.InsertedEvent.CustomerId.Should().BeNull();
    }

    /// <summary>
    /// A foreign-company person still fails with <c>CALENDAR_INVALID_LINK</c> (not <c>PERSON_CUSTOMER_MISMATCH</c>):
    /// existence is checked before the pair is resolved.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPersonIsInAnotherCompany_ReturnsInvalidLink()
    {
        var context = new LinkTestContext();
        // The person repository returns empty: the foreign-company person is filtered out by CompanyId != tenant.
        context.PersonRepositoryMock
            .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Person, bool>>>()))
            .ReturnsAsync(Array.Empty<Person>());

        var response = await context.CreateHandler().Handle(context.BuildCommand(
            requesterPersonId: Guid.NewGuid(),
            customerId: null), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.InvalidLink);
    }

    /// <summary>
    /// A customer from another company fails with <c>CALENDAR_INVALID_LINK</c> as well.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCustomerIsInAnotherCompany_ReturnsInvalidLink()
    {
        var context = new LinkTestContext();
        var foreignCustomer = Customer.Create(
            companyId: Guid.NewGuid(),
            personId: context.PersonId,
            userId: Guid.NewGuid(),
            customerCode: "C-FOR",
            customerStageId: Guid.NewGuid());
        EntityIdSetter.SetId(foreignCustomer, Guid.NewGuid());
        context.CustomerRepositoryMock.Setup(x => x.GetAsync(foreignCustomer.Id)).ReturnsAsync(foreignCustomer);

        var response = await context.CreateHandler().Handle(context.BuildCommand(
            requesterPersonId: null,
            customerId: foreignCustomer.Id), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.InvalidLink);
    }

    /// <summary>
    /// SPEC 67 — the DTO returned by the write endpoints exposes the resolved person name and customer code so
    /// the frontend can render the unified "Persona/Cliente" lookup without extra calls.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPersonAndCustomerResolved_DtoIncludesNameCodeAndActive()
    {
        var context = new LinkTestContext();
        var customer = context.AddCustomer(personId: context.PersonId, active: false, code: "C-OLD");

        var response = await context.CreateHandler().Handle(context.BuildCommand(
            requesterPersonId: context.PersonId,
            customerId: null), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.RequesterPersonId.Should().Be(context.PersonId);
        response.Data.RequesterPersonName.Should().Be("Ada Lovelace");
        response.Data.CustomerId.Should().Be(customer.Id);
        response.Data.CustomerCode.Should().Be("C-OLD");
        response.Data.IsCustomerActive.Should().BeFalse();
    }

    /// <summary>
    /// SPEC 67 — no link on the activity means the three fields stay null and the frontend renders nothing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoLinks_DtoLeavesNewFieldsNull()
    {
        var context = new LinkTestContext();

        var response = await context.CreateHandler().Handle(context.BuildCommand(
            requesterPersonId: null,
            customerId: null), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.RequesterPersonId.Should().BeNull();
        response.Data.RequesterPersonName.Should().BeNull();
        response.Data.CustomerId.Should().BeNull();
        response.Data.CustomerCode.Should().BeNull();
        response.Data.IsCustomerActive.Should().BeNull();
    }

    /// <summary>
    /// Minimal mocks for the create handler tests focused on the SPEC 67 link rules. The same set of mocks is
    /// enough to drive the <see cref="CalendarEventCommandProcessor"/> through
    /// <see cref="CreateCalendarEventCommandHandler"/> for any combination of person/customer ids.
    /// </summary>
    private sealed class LinkTestContext
    {
        public LinkTestContext()
        {
            CompanyId = Guid.NewGuid();
            ActorUserId = Guid.NewGuid();
            CalendarId = Guid.NewGuid();
            ActivityCompanyId = Guid.NewGuid();
            InitialStatusId = Guid.NewGuid();
            RescheduledStatusId = Guid.NewGuid();
            GlobalStatusId = Guid.NewGuid();
            GlobalActivityId = Guid.NewGuid();
            WebChannelId = Guid.NewGuid();
            PersonId = Guid.NewGuid();

            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(CompanyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(ActorUserId.ToString());

            CalendarCompany = new CalendarCompany
            {
                CompanyId = CompanyId,
                TimeZone = TimeZoneId,
                IsOpen24Hours = true,
                DefaultActivityUserId = ActorUserId,
                RescheduledByOthersStatusCompanyId = RescheduledStatusId,
                DefaultCalendarName = "Mi calendario"
            };
            CalendarCompanyRepositoryMock.SetupRows(new[] { CalendarCompany });

            UserCalendar = new UserCalendar
            {
                CompanyId = CompanyId,
                OwnerUserId = ActorUserId,
                Name = "Mi calendario",
                Color = "#1E88E5",
                IsActive = true
            };
            UserCalendarRepositoryMock.Setup(x => x.GetAsync(CalendarId)).ReturnsAsync(UserCalendar);

            CalendarUserConfiguration configuration = new()
            {
                CompanyId = CompanyId,
                UserId = ActorUserId,
                DefaultCalendarId = CalendarId,
                WorkDays = CalendarWorkDays.Weekdays
            };
            CalendarUserConfigurationRepositoryMock.SetupRows(new[] { configuration });

            ActivityCompany = new CalendarActivityCompany
            {
                CompanyId = CompanyId,
                CalendarActivityId = GlobalActivityId,
                Name = "Reunión",
                Color = "#1E88E5",
                DefaultDurationMinutes = 60,
                AllowsOverlap = false,
                RequiresBusinessHours = false,
                InitialCalendarStatusCompanyId = InitialStatusId,
                IsActive = true
            };
            ActivityCompanyRepositoryMock.Setup(x => x.GetAsync(ActivityCompanyId)).ReturnsAsync(ActivityCompany);

            CalendarActivity globalActivity = new()
            {
                Code = CalendarCodes.Activities.Meeting,
                Name = "Reunión",
                Color = "#1E88E5",
                DefaultDurationMinutes = 60,
                AllowsOverlap = false,
                RequiresBusinessHours = false,
                IsActive = true
            };
            EntityIdSetter.SetId(globalActivity, GlobalActivityId);
            CalendarActivityRepositoryMock.Setup(x => x.GetAsync(GlobalActivityId)).ReturnsAsync(globalActivity);

            InitialStatus = new CalendarStatusCompany
            {
                CompanyId = CompanyId,
                CalendarStatusId = GlobalStatusId,
                Name = "Pendiente",
                Color = "#FB8C00",
                IsActive = true
            };
            EntityIdSetter.SetId(InitialStatus, InitialStatusId);
            CalendarStatusCompanyRepositoryMock.Setup(x => x.GetAsync(InitialStatusId)).ReturnsAsync(InitialStatus);

            CalendarStatus globalStatus = new()
            {
                Code = CalendarCodes.Statuses.Pending,
                Name = "Pendiente",
                Color = "#FB8C00",
                BlocksTime = true,
                IsActive = true
            };
            EntityIdSetter.SetId(globalStatus, GlobalStatusId);
            CalendarStatusRepositoryMock.Setup(x => x.GetAsync(GlobalStatusId)).ReturnsAsync(globalStatus);

            WebChannel = new CommunicationChannel
            {
                Code = CommunicationChannelCodes.Web,
                Name = "Web",
                IsActive = true
            };
            EntityIdSetter.SetId(WebChannel, WebChannelId);
            CommunicationChannelRepositoryMock.SetupRows(new[] { WebChannel });
            CommunicationChannelRepositoryMock.Setup(x => x.GetAsync(WebChannelId)).ReturnsAsync(WebChannel);

            ApplicationUser actor = new()
            {
                Id = ActorUserId,
                FirstName = "Ada",
                LastName = "Lovelace",
                Email = "ada@x.com"
            };
            ApplicationUserRepositoryMock.Setup(x => x.GetAsync(ActorUserId)).ReturnsAsync(actor);

            // The person repo always returns the seeded Person (and any extra added through AddPerson).
            Person = new Person
            {
                CompanyId = CompanyId,
                PersonType = PersonType.Physical,
                FirstName = "Ada",
                LastName = "Lovelace"
            };
            EntityIdSetter.SetId(Person, PersonId);
            Persons.Add(Person);
            PersonRepositoryMock
                .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Person, bool>>>()))
                .ReturnsAsync(() => Persons);

            CalendarEventsRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<CalendarEvent>()))
                .Callback<CalendarEvent>(e => InsertedEvent = e)
                .ReturnsAsync(true);
            CalendarEventLogRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<CalendarEventLog>())).ReturnsAsync(true);

            UnitOfWorkMock.Setup(x => x.CalendarEvents).Returns(CalendarEventsRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CommunicationChannel>()).Returns(CommunicationChannelRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarCompany>()).Returns(CalendarCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarUserConfiguration>()).Returns(CalendarUserConfigurationRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<UserCalendar>()).Returns(UserCalendarRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivityCompany>()).Returns(ActivityCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivity>()).Returns(CalendarActivityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatusCompany>()).Returns(CalendarStatusCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatus>()).Returns(CalendarStatusRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationUser>()).Returns(ApplicationUserRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(PersonRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Customer>()).Returns(CustomerRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Ticket>()).Returns(TicketRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Project>()).Returns(ProjectRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<UserCompany>()).Returns(UserCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarEventLog>()).Returns(CalendarEventLogRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            UnitOfWorkMock
                .Setup(x => x.CalendarEvents.GetScheduleRowsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CalendarEventScheduleRow>());
            UnitOfWorkMock
                .Setup(x => x.AcquireScheduleLockAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        public Guid CompanyId { get; }
        public Guid ActorUserId { get; }
        public Guid CalendarId { get; }
        public Guid ActivityCompanyId { get; }
        public Guid InitialStatusId { get; }
        public Guid RescheduledStatusId { get; }
        public Guid GlobalStatusId { get; }
        public Guid GlobalActivityId { get; }
        public Guid WebChannelId { get; }
        public Guid PersonId { get; }
        public Person Person { get; }

        public CalendarCompany CalendarCompany { get; }
        public UserCalendar UserCalendar { get; }
        public CalendarActivityCompany ActivityCompany { get; }
        public CalendarStatusCompany InitialStatus { get; }
        public CommunicationChannel WebChannel { get; }

        public CalendarEvent? InsertedEvent { get; private set; }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICalendarEventRepository> CalendarEventsRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CommunicationChannel>> CommunicationChannelRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarCompany>> CalendarCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarUserConfiguration>> CalendarUserConfigurationRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserCalendar>> UserCalendarRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarActivityCompany>> ActivityCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarActivity>> CalendarActivityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatusCompany>> CalendarStatusCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatus>> CalendarStatusRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> ApplicationUserRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Person>> PersonRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Customer>> CustomerRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Ticket>> TicketRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Project>> ProjectRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserCompany>> UserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarEventLog>> CalendarEventLogRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ICalendarRecurrenceExpander> ExpanderMock { get; } = new();

        private List<Person> Persons { get; } = new();

        public void AddPerson(Guid otherPersonId)
        {
            var p = new Person
            {
                CompanyId = CompanyId,
                PersonType = PersonType.Physical,
                FirstName = "Grace",
                LastName = "Hopper"
            };
            EntityIdSetter.SetId(p, otherPersonId);
            Persons.Add(p);
        }

        public Customer AddCustomer(Guid personId, bool active, string code)
        {
            var customer = Customer.Create(
                companyId: CompanyId,
                personId: personId,
                userId: Guid.NewGuid(),
                customerCode: code,
                customerStageId: Guid.NewGuid());
            if (!active)
            {
                customer.Deactivate();
            }
            EntityIdSetter.SetId(customer, Guid.NewGuid());
            CustomerRepositoryMock.Setup(x => x.GetAsync(customer.Id)).ReturnsAsync(customer);
            CustomerRepositoryMock
                .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Customer, bool>>>()))
                .ReturnsAsync(new[] { customer });
            return customer;
        }

        public CreateCalendarEventCommandHandler CreateHandler()
        {
            ExpanderMock.Setup(x => x.ValidateRule(It.IsAny<string>())).Returns((string r) => CalendarRecurrenceRule.Parse(r));
            var companyGuard = new CalendarCompanyGuard(UnitOfWorkMock.Object);
            var catalogReader = new CalendarCatalogReader(UnitOfWorkMock.Object);
            var initialStatusResolver = new CalendarEventInitialStatusResolver(UnitOfWorkMock.Object, catalogReader);
            var scheduleGuard = new CalendarEventScheduleGuard(UnitOfWorkMock.Object, ExpanderMock.Object);
            var logWriter = new CalendarEventLogWriter(UnitOfWorkMock.Object);
            var processor = new CalendarEventCommandProcessor(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                companyGuard,
                catalogReader,
                initialStatusResolver,
                scheduleGuard,
                logWriter,
                ExpanderMock.Object);
            return new CreateCalendarEventCommandHandler(processor);
        }

        public CreateCalendarEventCommand BuildCommand(Guid? requesterPersonId, Guid? customerId)
        {
            var start = new DateTimeOffset(2026, 6, 15, 14, 0, 0, TimeSpan.Zero);
            return new CreateCalendarEventCommand
            {
                CalendarId = CalendarId,
                CalendarActivityCompanyId = ActivityCompanyId,
                Title = "Sync",
                Start = start,
                End = start.AddMinutes(60),
                IsAllDay = false,
                RequesterPersonId = requesterPersonId,
                CustomerId = customerId
            };
        }
    }
}