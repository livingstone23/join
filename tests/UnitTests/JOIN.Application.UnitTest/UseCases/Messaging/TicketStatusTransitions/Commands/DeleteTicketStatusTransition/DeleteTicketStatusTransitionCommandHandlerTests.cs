using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Commands.DeleteTicketStatusTransition;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketStatusTransitions.Commands.DeleteTicketStatusTransition;

/// <summary>
/// Unit tests for <see cref="DeleteTicketStatusTransitionCommandHandler"/>. Covers
/// <c>COMPANY_REQUIRED</c>, <c>TICKET_STATUS_TRANSITION_NOT_FOUND</c> (when the row
/// does not exist or belongs to a different tenant), and the happy path that soft
/// deletes and returns the id.
/// </summary>
public sealed class DeleteTicketStatusTransitionCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IGenericRepository<TicketStatusTransition>> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();

    public DeleteTicketStatusTransitionCommandHandlerTests()
    {
        _unitOfWorkMock.Setup(x => x.GetRepository<TicketStatusTransition>()).Returns(_repositoryMock.Object);
        _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TicketStatusTransition>())).ReturnsAsync(true);
        _unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        _currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        var handler = new DeleteTicketStatusTransitionCommandHandler(_unitOfWorkMock.Object, _currentUserServiceMock.Object);

        var response = await handler.Handle(new DeleteTicketStatusTransitionCommand(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenRowDoesNotExist_ShouldReturnNotFound()
    {
        var tenantId = Guid.NewGuid();
        _currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        _repositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync((TicketStatusTransition?)null);
        var handler = new DeleteTicketStatusTransitionCommandHandler(_unitOfWorkMock.Object, _currentUserServiceMock.Object);

        var response = await handler.Handle(new DeleteTicketStatusTransitionCommand(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_STATUS_TRANSITION_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenRowExistsAndBelongsToTenant_ShouldSoftDeleteAndReturnId()
    {
        var tenantId = Guid.NewGuid();
        var id = Guid.NewGuid();
        _currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        var entity = new TicketStatusTransition
        {
            CompanyId = tenantId,
            FromStatusId = Guid.NewGuid(),
            ToStatusId = Guid.NewGuid()
        };
        _repositoryMock.Setup(x => x.GetAsync(id)).ReturnsAsync(entity);
        var handler = new DeleteTicketStatusTransitionCommandHandler(_unitOfWorkMock.Object, _currentUserServiceMock.Object);

        var response = await handler.Handle(new DeleteTicketStatusTransitionCommand(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        _repositoryMock.Verify(x => x.UpdateAsync(It.Is<TicketStatusTransition>(t => t.GcRecord != 0)), Times.Once);
    }
}