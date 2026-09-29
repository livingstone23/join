using System.Linq.Expressions;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Domain.Exceptions;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets;

/// <summary>
/// Unit tests for <see cref="TicketCodeGenerator"/>: per-company numbering driven by
/// <see cref="TicketCompanyDefault"/> (<c>StartCode</c>, <c>CodeSequenceLength</c>), where the
/// next sequence is the highest one already used plus one, soft-deleted tickets included.
/// </summary>
public sealed class TicketCodeGeneratorTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);

    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the standard <c>TICK-yyyyMM-NNNN</c> format starting at 1 when the company has
    /// no <see cref="TicketCompanyDefault"/> row and no tickets under the current month.
    /// </summary>
    [Fact]
    public async Task AssignCodeAsync_WhenNoDefaultsAndNoTickets_ShouldAssignFirstStandardCode()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GeneratorTestContext();
        var ticket = new Ticket { CompanyId = companyId };

        // Act
        await context.CreateGenerator().AssignCodeAsync(ticket, Now, CancellationToken.None);

        // Assert
        ticket.Code.Should().Be("TICK-202609-0001");
    }

    /// <summary>
    /// Verifies that the standard sequence continues from the highest code in use, including a
    /// soft-deleted ticket, instead of counting active tickets (which reissued deleted codes).
    /// </summary>
    [Fact]
    public async Task AssignCodeAsync_WhenHighestStandardCodeIsSoftDeleted_ShouldContinueAfterIt()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var active = CreateStandardTicket(companyId, 3);
        var deleted = CreateStandardTicket(companyId, 8);
        deleted.MarkAsDeleted();

        var context = new GeneratorTestContext();
        context.SetupCompanyTickets(active, deleted);
        var ticket = new Ticket { CompanyId = companyId };

        // Act
        await context.CreateGenerator().AssignCodeAsync(ticket, Now, CancellationToken.None);

        // Assert
        ticket.Code.Should().Be("TICK-202609-0009");
    }

    /// <summary>
    /// Verifies the personalized format: the configured <c>StartCode</c> (normalized), padded to
    /// <c>CodeSequenceLength</c>, continuing after the highest numeric suffix; codes with a
    /// non-numeric suffix under the same prefix, or under another prefix, are ignored.
    /// </summary>
    [Fact]
    public async Task AssignCodeAsync_WhenPersonalizedCodeConfigured_ShouldUseStartCodeAndLength()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GeneratorTestContext();
        context.SetupDefaults(CreateDefaults(companyId, " sup ", 5));
        context.SetupCompanyTickets(
            CreatePersonalizedTicket(companyId, "SUP", 41, 5),
            CreateTicketWithRawCode(companyId, "SUP-ABCDE"),
            CreateStandardTicket(companyId, 99));
        var ticket = new Ticket { CompanyId = companyId };

        // Act
        await context.CreateGenerator().AssignCodeAsync(ticket, Now, CancellationToken.None);

        // Assert
        ticket.Code.Should().Be("SUP-00042");
    }

    /// <summary>
    /// Verifies that the personalized sequence is continuous per company: a code created in an
    /// earlier month still counts, so numbering never restarts at 1 each month.
    /// </summary>
    [Fact]
    public async Task AssignCodeAsync_WhenPersonalizedCodeFromPreviousMonthExists_ShouldNotRestartSequence()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var previousMonth = CreatePersonalizedTicket(companyId, "TICK", 8, 6);
        previousMonth.Created = Now.AddMonths(-1);

        var context = new GeneratorTestContext();
        context.SetupDefaults(CreateDefaults(companyId, "TICK", 6));
        context.SetupCompanyTickets(previousMonth);
        var ticket = new Ticket { CompanyId = companyId };

        // Act
        await context.CreateGenerator().AssignCodeAsync(ticket, Now, CancellationToken.None);

        // Assert
        ticket.Code.Should().Be("TICK-000009");
    }

    /// <summary>
    /// Verifies that another company's <see cref="TicketCompanyDefault"/> is ignored, and that the
    /// ticket lookup is scoped to the ticket's company and to the prefix being generated.
    /// </summary>
    [Fact]
    public async Task AssignCodeAsync_ShouldScopeDefaultsAndTicketLookupToTheTicketCompany()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var otherCompanyId = _fixture.Create<Guid>();
        var context = new GeneratorTestContext();
        context.SetupDefaults(CreateDefaults(otherCompanyId, "OTHER", 4));

        Expression<Func<Ticket, bool>>? capturedPredicate = null;
        context.TicketRepositoryMock
            .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<Expression<Func<Ticket, bool>>>()))
            .Callback<Expression<Func<Ticket, bool>>>(predicate => capturedPredicate = predicate)
            .ReturnsAsync(Array.Empty<Ticket>());

        var ticket = new Ticket { CompanyId = companyId };

        // Act
        await context.CreateGenerator().AssignCodeAsync(ticket, Now, CancellationToken.None);

        // Assert
        ticket.Code.Should().Be("TICK-202609-0001", "the other company's personalized defaults must not apply");

        var predicate = capturedPredicate!.Compile();
        predicate(CreateStandardTicket(companyId, 5)).Should().BeTrue();
        predicate(CreateStandardTicket(otherCompanyId, 5)).Should().BeFalse("each company keeps its own numbering");
        predicate(CreatePersonalizedTicket(companyId, "SUP", 5, 5)).Should().BeFalse("only codes under the generated prefix count");
    }

    /// <summary>
    /// Verifies that a personalized configuration without a usable <c>StartCode</c> surfaces the
    /// domain error instead of silently generating a code.
    /// </summary>
    [Fact]
    public async Task AssignCodeAsync_WhenPersonalizedStartCodeIsBlank_ShouldThrowDomainException()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GeneratorTestContext();
        context.SetupDefaults(CreateDefaults(companyId, "   ", 6));
        var ticket = new Ticket { CompanyId = companyId };

        // Act
        var act = () => context.CreateGenerator().AssignCodeAsync(ticket, Now, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("INVALID_TICKET_PREFIX");
    }

    private static TicketCompanyDefault CreateDefaults(Guid companyId, string startCode, int length) => new()
    {
        CompanyId = companyId,
        StartCode = startCode,
        CodeSequenceLength = length,
        UsePersonalizedCode = true,
        GcRecord = 0
    };

    private static Ticket CreateStandardTicket(Guid companyId, int sequence)
    {
        var ticket = new Ticket { CompanyId = companyId, Created = Now };
        ticket.SetStandardCode(Now.Year, Now.Month, sequence);
        return ticket;
    }

    private static Ticket CreatePersonalizedTicket(Guid companyId, string startCode, int sequence, int length)
    {
        var ticket = new Ticket { CompanyId = companyId, Created = Now };
        ticket.SetPersonalizedCode(startCode, sequence, length);
        return ticket;
    }

    private static Ticket CreateTicketWithRawCode(Guid companyId, string code)
    {
        // Code has a private setter; the personalized setter cannot produce a non-numeric
        // suffix, so the value is injected to emulate legacy or manually edited data.
        var ticket = new Ticket { CompanyId = companyId, Created = Now };
        typeof(Ticket).GetProperty(nameof(Ticket.Code))!.SetValue(ticket, code);
        return ticket;
    }

    private sealed class GeneratorTestContext
    {
        public GeneratorTestContext()
        {
            UnitOfWorkMock.Setup(x => x.GetRepository<Ticket>()).Returns(TicketRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketCompanyDefault>()).Returns(DefaultsRepositoryMock.Object);
            DefaultsRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketCompanyDefault>());
            TicketRepositoryMock
                .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<Expression<Func<Ticket, bool>>>()))
                .ReturnsAsync(Array.Empty<Ticket>());
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Ticket>> TicketRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketCompanyDefault>> DefaultsRepositoryMock { get; } = new();

        public void SetupDefaults(params TicketCompanyDefault[] defaults) =>
            DefaultsRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(defaults);

        // The mock returns the rows as-is (the real repository applies the predicate in SQL),
        // which lets these tests prove the in-memory max computation on its own.
        public void SetupCompanyTickets(params Ticket[] tickets) =>
            TicketRepositoryMock
                .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<Expression<Func<Ticket, bool>>>()))
                .ReturnsAsync(tickets);

        public TicketCodeGenerator CreateGenerator() => new(UnitOfWorkMock.Object);
    }
}
