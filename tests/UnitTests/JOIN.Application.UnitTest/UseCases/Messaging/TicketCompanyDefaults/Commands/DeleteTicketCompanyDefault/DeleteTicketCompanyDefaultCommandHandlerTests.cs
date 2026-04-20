using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketCompanyDefaults.Commands.DeleteTicketCompanyDefault;

/// <summary>
/// Contains the unit tests for the tenant ticket-default delete flow.
/// </summary>
public sealed class DeleteTicketCompanyDefaultCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the authenticated context has no valid company claim.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyContextIsMissing_ShouldReturnCompanyRequiredError()
    {
        var context = new DeleteTicketCompanyDefaultCommandTestContext(Guid.Empty, isAuthenticated: false);
        var handler = context.CreateHandler();

        var response = await handler.Handle(new DeleteTicketCompanyDefaultCommand(_fixture.Create<Guid>()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");

        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TicketCompanyDefault>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the tenant not-found branch for null, deleted, and wrong-tenant entities.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("deleted")]
    [InlineData("other-tenant")]
    public async Task Handle_WhenConfigurationIsUnavailableForCurrentTenant_ShouldReturnNotFoundError(string scenario)
    {
        var companyId = _fixture.Create<Guid>();
        var request = new DeleteTicketCompanyDefaultCommand(_fixture.Create<Guid>());
        var context = new DeleteTicketCompanyDefaultCommandTestContext(companyId, isAuthenticated: true);

        TicketCompanyDefault? entity = scenario switch
        {
            "deleted" => new TicketCompanyDefault
            {
                CompanyId = companyId,
                GcRecord = 20260420,
                StartCode = "JOIN",
                CodeSequenceLength = 6
            },
            "other-tenant" => new TicketCompanyDefault
            {
                CompanyId = _fixture.Create<Guid>(),
                GcRecord = 0,
                StartCode = "JOIN",
                CodeSequenceLength = 6
            },
            _ => null
        };

        context.RepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_COMPANY_DEFAULT_NOT_FOUND");
        response.Errors.Should().Contain("Ticket company default configuration not found for the current tenant.");

        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TicketCompanyDefault>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence-failure branch after the entity is soft-deleted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        var companyId = _fixture.Create<Guid>();
        var entity = new TicketCompanyDefault
        {
            CompanyId = companyId,
            GcRecord = 0,
            StartCode = "JOIN",
            CodeSequenceLength = 6
        };

        var request = new DeleteTicketCompanyDefaultCommand(entity.Id);
        var context = new DeleteTicketCompanyDefaultCommandTestContext(companyId, isAuthenticated: true);

        context.RepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        response.Errors.Should().Contain("No records were affected while deleting the configuration.");
        entity.GcRecord.Should().NotBe(0);

        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies the successful soft-delete path.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldSoftDeleteConfigurationAndReturnId()
    {
        var companyId = _fixture.Create<Guid>();
        var entity = new TicketCompanyDefault
        {
            CompanyId = companyId,
            GcRecord = 0,
            StartCode = "JOIN",
            CodeSequenceLength = 6
        };

        var request = new DeleteTicketCompanyDefaultCommand(entity.Id);
        var context = new DeleteTicketCompanyDefaultCommandTestContext(companyId, isAuthenticated: true);

        context.RepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket company default configuration deleted successfully.");
        response.Data.Should().Be(entity.Id);
        entity.GcRecord.Should().NotBe(0);

        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Holds the reusable mocks for the delete handler tests.
    /// </summary>
    private sealed class DeleteTicketCompanyDefaultCommandTestContext
    {
        public DeleteTicketCompanyDefaultCommandTestContext(Guid companyId, bool isAuthenticated)
        {
            RepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TicketCompanyDefault>())).ReturnsAsync(true);

            UnitOfWorkMock.Setup(x => x.GetRepository<TicketCompanyDefault>()).Returns(RepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            CurrentUserServiceMock.Setup(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.Setup(x => x.IsAuthenticated).Returns(isAuthenticated);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<TicketCompanyDefault>> RepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public DeleteTicketCompanyDefaultCommandHandler CreateHandler()
        {
            return new DeleteTicketCompanyDefaultCommandHandler(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
        }
    }
}
