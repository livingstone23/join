using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Commands.CreateTicketStatusTransition;
using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketStatusTransitions.Commands.CreateTicketStatusTransition;

/// <summary>
/// Unit tests for <see cref="CreateTicketStatusTransitionCommandHandler"/>. Covers
/// the four rejection branches (<c>COMPANY_REQUIRED</c>, <c>SAME_STATUS_TRANSITION</c>,
/// <c>INVALID_FROM_STATUS</c>, <c>INVALID_TO_STATUS</c>, <c>TICKET_STATUS_TRANSITION_DUPLICATE</c>)
/// and the happy path that inserts the row and projects the DTO with the joined status names.
/// </summary>
public sealed class CreateTicketStatusTransitionCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IGenericRepository<TicketStatus>> _statusRepositoryMock = new();
    private readonly Mock<IGenericRepository<TicketStatusTransition>> _transitionRepositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();

    public CreateTicketStatusTransitionCommandHandlerTests()
    {
        _unitOfWorkMock.Setup(x => x.GetRepository<TicketStatus>()).Returns(_statusRepositoryMock.Object);
        _unitOfWorkMock.Setup(x => x.GetRepository<TicketStatusTransition>()).Returns(_transitionRepositoryMock.Object);
        _transitionRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketStatusTransition>())).ReturnsAsync(true);
        _unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        _currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        var handler = new CreateTicketStatusTransitionCommandHandler(_unitOfWorkMock.Object, _currentUserServiceMock.Object);

        var response = await handler.Handle(
            new CreateTicketStatusTransitionCommand { FromStatusId = Guid.NewGuid(), ToStatusId = Guid.NewGuid() },
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenFromEqualsTo_ShouldReturnSameStatusTransition()
    {
        var tenantId = Guid.NewGuid();
        _currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        var statusId = Guid.NewGuid();
        var handler = new CreateTicketStatusTransitionCommandHandler(_unitOfWorkMock.Object, _currentUserServiceMock.Object);

        var response = await handler.Handle(
            new CreateTicketStatusTransitionCommand { FromStatusId = statusId, ToStatusId = statusId },
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("SAME_STATUS_TRANSITION");
    }

    [Fact]
    public async Task Handle_WhenFromStatusDoesNotExist_ShouldReturnInvalidFromStatus()
    {
        var tenantId = Guid.NewGuid();
        _currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        var fromStatusId = Guid.NewGuid();
        var toStatusId = Guid.NewGuid();
        var toStatus = new TicketStatus { CompanyId = tenantId, Name = "Closed", IsFinal = true };
        SetEntityId(toStatus, toStatusId);
        _statusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { toStatus });
        var handler = new CreateTicketStatusTransitionCommandHandler(_unitOfWorkMock.Object, _currentUserServiceMock.Object);

        var response = await handler.Handle(
            new CreateTicketStatusTransitionCommand { FromStatusId = fromStatusId, ToStatusId = toStatusId },
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_FROM_STATUS");
    }

    [Fact]
    public async Task Handle_WhenToStatusDoesNotExist_ShouldReturnInvalidToStatus()
    {
        var tenantId = Guid.NewGuid();
        _currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        var fromStatusId = Guid.NewGuid();
        var toStatusId = Guid.NewGuid();
        var fromStatus = new TicketStatus { CompanyId = tenantId, Name = "Open", IsFinal = false };
        SetEntityId(fromStatus, fromStatusId);
        _statusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { fromStatus });
        var handler = new CreateTicketStatusTransitionCommandHandler(_unitOfWorkMock.Object, _currentUserServiceMock.Object);

        var response = await handler.Handle(
            new CreateTicketStatusTransitionCommand { FromStatusId = fromStatusId, ToStatusId = toStatusId },
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_TO_STATUS");
    }

    [Fact]
    public async Task Handle_WhenDuplicateRuleExists_ShouldReturnDuplicate()
    {
        var tenantId = Guid.NewGuid();
        _currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        var fromStatusId = Guid.NewGuid();
        var toStatusId = Guid.NewGuid();
        var fromStatus = new TicketStatus { CompanyId = tenantId, Name = "Open", IsFinal = false };
        SetEntityId(fromStatus, fromStatusId);
        var toStatus = new TicketStatus { CompanyId = tenantId, Name = "InProgress", IsFinal = false };
        SetEntityId(toStatus, toStatusId);
        _statusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { fromStatus, toStatus });
        _transitionRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new TicketStatusTransition { CompanyId = tenantId, FromStatusId = fromStatusId, ToStatusId = toStatusId }
            });
        var handler = new CreateTicketStatusTransitionCommandHandler(_unitOfWorkMock.Object, _currentUserServiceMock.Object);

        var response = await handler.Handle(
            new CreateTicketStatusTransitionCommand { FromStatusId = fromStatusId, ToStatusId = toStatusId },
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_STATUS_TRANSITION_DUPLICATE");
    }

    [Fact]
    public async Task Handle_WhenAllGatesPass_ShouldInsertAndReturnDtoWithJoinedStatusNames()
    {
        var tenantId = Guid.NewGuid();
        _currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        var fromStatusId = Guid.NewGuid();
        var toStatusId = Guid.NewGuid();
        var fromStatus = new TicketStatus { CompanyId = tenantId, Name = "Open", IsFinal = false };
        SetEntityId(fromStatus, fromStatusId);
        var toStatus = new TicketStatus { CompanyId = tenantId, Name = "InProgress", IsFinal = false };
        SetEntityId(toStatus, toStatusId);
        _statusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { fromStatus, toStatus });
        _transitionRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<TicketStatusTransition>());
        var handler = new CreateTicketStatusTransitionCommandHandler(_unitOfWorkMock.Object, _currentUserServiceMock.Object);

        var response = await handler.Handle(
            new CreateTicketStatusTransitionCommand { FromStatusId = fromStatusId, ToStatusId = toStatusId },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.FromStatusName.Should().Be("Open");
        response.Data!.ToStatusName.Should().Be("InProgress");
        _transitionRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketStatusTransition>()), Times.Once);
    }

    private static void SetEntityId(BaseEntity entity, Guid id)
        => typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(entity, id);
}