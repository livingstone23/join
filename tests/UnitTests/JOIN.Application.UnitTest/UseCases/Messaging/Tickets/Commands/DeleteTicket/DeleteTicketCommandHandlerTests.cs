using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.Tickets.Commands;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.DeleteTicket;

/// <summary>
/// Contains the unit tests for the logical delete flow of tickets.
/// The suite validates tenant checks, not-found protection, and persistence outcomes.
/// </summary>
public sealed class DeleteTicketCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the happy path for a logical delete.
    /// This test ensures the ticket is marked as deleted and the change is persisted successfully.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketExists_ShouldMarkTicketAsDeletedAndReturnSuccess()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();
        var context = new DeleteTicketCommandHandlerTestContext(companyId);
        var ticket = CreateTicket(ticketId, companyId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.TicketRepositoryMock
            .Setup(x => x.GetAsync(ticketId))
            .ReturnsAsync(ticket);

        context.TicketRepositoryMock
            .Setup(x => x.UpdateAsync(ticket))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTicketCommand(ticketId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket deleted successfully.");
        response.Data.Should().Be(ticketId);
        ticket.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);

        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(ticket), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies the early exit when the tenant identifier is missing.
    /// This protects the delete flow from running without multi-tenant context.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new DeleteTicketCommandHandlerTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTicketCommand(_fixture.Create<Guid>()), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.UnitOfWorkMock.Verify(x => x.GetRepository<Company>(), Times.Never);
    }

    /// <summary>
    /// Verifies the error path when the company does not exist.
    /// This prevents the logical delete from continuing for an invalid tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ShouldReturnInvalidCompanyError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteTicketCommandHandlerTestContext(companyId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync((Company?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTicketCommand(_fixture.Create<Guid>()), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY");
        context.TicketRepositoryMock.Verify(x => x.GetAsync(It.IsAny<Guid>()), Times.Never);
    }

    /// <summary>
    /// Verifies the not-found branch when the ticket does not belong to the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketBelongsToAnotherCompany_ShouldReturnTicketNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();
        var context = new DeleteTicketCommandHandlerTestContext(companyId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.TicketRepositoryMock
            .Setup(x => x.GetAsync(ticketId))
            .ReturnsAsync(CreateTicket(ticketId, Guid.NewGuid()));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTicketCommand(ticketId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_NOT_FOUND");
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Ticket>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when the delete operation affects no records.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();
        var context = new DeleteTicketCommandHandlerTestContext(companyId);
        var ticket = CreateTicket(ticketId, companyId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.TicketRepositoryMock
            .Setup(x => x.GetAsync(ticketId))
            .ReturnsAsync(ticket);

        context.TicketRepositoryMock
            .Setup(x => x.UpdateAsync(ticket))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTicketCommand(ticketId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(ticket), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid ticket entity owned by the provided company.
    /// </summary>
    private static Ticket CreateTicket(Guid ticketId, Guid companyId)
    {
        var ticket = new Ticket
        {
            CompanyId = companyId,
            Name = "Delete me",
            Description = "Logical delete ticket",
            CreatedByUserId = Guid.NewGuid(),
            TimeUnitId = Guid.NewGuid(),
            TicketStatusId = Guid.NewGuid(),
            TicketComplexityId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid()
        };

        ticket.SetStandardCode(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        typeof(BaseEntity)
            .GetProperty("Id")!
            .SetValue(ticket, ticketId);

        return ticket;
    }

    /// <summary>
    /// Holds the reusable mocks used by the delete handler tests.
    /// </summary>
    private sealed class DeleteTicketCommandHandlerTestContext
    {
        public DeleteTicketCommandHandlerTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, TicketRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Ticket>> TicketRepositoryMock { get; } = new();

        public DeleteTicketCommandHandler CreateHandler()
        {
            return new DeleteTicketCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object);
        }

        private static void SetupRepository<TEntity>(
            Mock<IUnitOfWork> unitOfWorkMock,
            Mock<IGenericRepository<TEntity>> repositoryMock)
            where TEntity : class
        {
            unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
        }
    }
}
