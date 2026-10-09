using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.CommunicationChannels.Commands;
using JOIN.Domain.Common;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;
using JOIN.Domain.Support;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.CommunicationChannels.Commands.DeleteCommunicationChannel;

/// <summary>
/// Contains the unit tests for the communication channel soft-delete command handler.
/// These tests verify not-found protection, persistence failures, and the successful delete flow.
/// </summary>
public sealed class DeleteCommunicationChannelCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the requested communication channel does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCommunicationChannelDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var channelId = _fixture.Create<Guid>();
        var request = new DeleteCommunicationChannelCommand(channelId);
        var context = new DeleteCommunicationChannelCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(channelId))
            .ReturnsAsync((CommunicationChannel?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMMUNICATIONCHANNEL_NOT_FOUND");
        response.Errors.Should().Contain("Communication channel not found.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CommunicationChannel>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var entity = new CommunicationChannel
        {
            Name = "WhatsApp",
            IsActive = true,
            GcRecord = 0
        };

        var request = new DeleteCommunicationChannelCommand(entity.Id);
        var context = new DeleteCommunicationChannelCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.UpdateAsync(entity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        response.Errors.Should().Contain("No records were affected while deleting the communication channel.");
    }

    /// <summary>
    /// Verifies the happy path when the communication channel is successfully soft-deleted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldSoftDeleteCommunicationChannelAndReturnId()
    {
        // Arrange
        var entity = new CommunicationChannel
        {
            Name = "WhatsApp",
            IsActive = true,
            GcRecord = 0
        };

        var request = new DeleteCommunicationChannelCommand(entity.Id);
        var context = new DeleteCommunicationChannelCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.UpdateAsync(entity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Communication channel deleted successfully.");
        response.Data.Should().Be(entity.Id);

        entity.GcRecord.Should().NotBe(0);

        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// SPEC 41: every active child type blocks the delete and is listed in the errors.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActiveDependentsExist_ShouldReturnInUseWithDetails()
    {
        // Arrange
        var entity = new CommunicationChannel
        {
            Name = "WhatsApp",
            IsActive = true,
            GcRecord = 0
        };

        var request = new DeleteCommunicationChannelCommand(entity.Id);
        var context = new DeleteCommunicationChannelCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.UpdateAsync(entity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Arrange: one row per child type that blocks the delete (SPEC 41).
        var child1 = new Ticket { CompanyId = Guid.NewGuid(), ChannelId = entity.Id };
        context.UnitOfWorkMock.SetupRepositoryRows<Ticket>([child1]);
        var child2 = new TicketCompanyDefault { CompanyId = Guid.NewGuid(), ChannelDefaultId = entity.Id };
        context.UnitOfWorkMock.SetupRepositoryRows<TicketCompanyDefault>([child2]);
        var child3 = new TicketNotification { CompanyId = Guid.NewGuid(), CommunicationChannelId = entity.Id };
        context.UnitOfWorkMock.SetupRepositoryRows<TicketNotification>([child3]);
        var child4 = new UserCommunicationChannel { CompanyId = Guid.NewGuid(), UserId = Guid.NewGuid(), CommunicationChannelId = entity.Id };
        context.UnitOfWorkMock.SetupRepositoryRows<UserCommunicationChannel>([child4]);


        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMMUNICATIONCHANNEL_IN_USE");
        response.Errors.Should().Equal("Active tickets: 1", "Active ticket company defaults: 1", "Active ticket notifications: 1", "Active user communication channels: 1");
        entity.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
    }

    /// <summary>
    /// SPEC 41: logically deleted children do not block the delete.
    /// </summary>
    [Fact]
    public async Task Handle_WhenDependentsAreDeleted_ShouldSoftDelete()
    {
        // Arrange
        var entity = new CommunicationChannel
        {
            Name = "WhatsApp",
            IsActive = true,
            GcRecord = 0
        };

        var request = new DeleteCommunicationChannelCommand(entity.Id);
        var context = new DeleteCommunicationChannelCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.UpdateAsync(entity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Arrange: one row per child type that blocks the delete (SPEC 41).
        var child1 = new Ticket { CompanyId = Guid.NewGuid(), ChannelId = entity.Id };
        child1.MarkAsDeleted();
        context.UnitOfWorkMock.SetupRepositoryRows<Ticket>([child1]);
        var child2 = new TicketCompanyDefault { CompanyId = Guid.NewGuid(), ChannelDefaultId = entity.Id };
        child2.MarkAsDeleted();
        context.UnitOfWorkMock.SetupRepositoryRows<TicketCompanyDefault>([child2]);
        var child3 = new TicketNotification { CompanyId = Guid.NewGuid(), CommunicationChannelId = entity.Id };
        child3.MarkAsDeleted();
        context.UnitOfWorkMock.SetupRepositoryRows<TicketNotification>([child3]);
        var child4 = new UserCommunicationChannel { CompanyId = Guid.NewGuid(), UserId = Guid.NewGuid(), CommunicationChannelId = entity.Id };
        child4.MarkAsDeleted();
        context.UnitOfWorkMock.SetupRepositoryRows<UserCommunicationChannel>([child4]);


        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Communication channel deleted successfully.");
        response.Data.Should().Be(entity.Id);

        entity.GcRecord.Should().NotBe(0);

        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Registers a repository in the mocked unit of work using the generic resolution pattern.
    /// </summary>
    private static void SetupRepository<TEntity>(Mock<IUnitOfWork> unitOfWorkMock, Mock<IGenericRepository<TEntity>> repositoryMock)
        where TEntity : class
    {
        unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the delete handler.
    /// </summary>
    private sealed class DeleteCommunicationChannelCommandTestContext
    {
        public DeleteCommunicationChannelCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<IGenericRepository<CommunicationChannel>> RepositoryMock { get; } = new();

        public DeleteCommunicationChannelCommandHandler CreateHandler()
        {
            return new DeleteCommunicationChannelCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
