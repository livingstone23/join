using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.CommunicationChannels.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.CommunicationChannels.Commands.UpdateCommunicationChannel;

/// <summary>
/// Contains the unit tests for the communication channel update command handler.
/// These tests verify not-found protection, duplicate-name validation,
/// persistence failures, and the successful update flow.
/// </summary>
public sealed class UpdateCommunicationChannelCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the requested communication channel does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCommunicationChannelDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var request = CreateValidCommand(_fixture.Create<Guid>());
        var context = new UpdateCommunicationChannelCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(request.Id))
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
    /// Verifies the duplicate-name branch when another channel already uses the same name.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherChannelUsesSameName_ShouldReturnNameInUseError()
    {
        // Arrange
        var entity = new CommunicationChannel
        {
            Name = "Telegram",
            Provider = "OldProvider",
            Code = "TG-001",
            IsActive = true,
            GcRecord = 0
        };

        var request = CreateValidCommand(entity.Id);
        var context = new UpdateCommunicationChannelCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                entity,
                new CommunicationChannel
                {
                    Name = request.Name.Trim(),
                    GcRecord = 0
                }
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMMUNICATIONCHANNEL_NAME_IN_USE");
        response.Errors.Should().Contain("Another active communication channel already uses the same name.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CommunicationChannel>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var entity = new CommunicationChannel
        {
            Name = "Telegram",
            Provider = "OldProvider",
            Code = "TG-001",
            IsActive = false,
            GcRecord = 0
        };

        var request = CreateValidCommand(entity.Id);
        var context = new UpdateCommunicationChannelCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync([entity]);

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
        response.Message.Should().Be("UPDATE_FAILED");
        response.Errors.Should().Contain("No records were affected while updating the communication channel.");
    }

    /// <summary>
    /// Verifies the happy path and normalization behavior when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateCommunicationChannelAndReturnDto()
    {
        // Arrange
        var entity = new CommunicationChannel
        {
            Name = "Telegram",
            Provider = "OldProvider",
            Code = "TG-001",
            IsActive = false,
            GcRecord = 0
        };

        var request = CreateValidCommand(entity.Id);
        var context = new UpdateCommunicationChannelCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync([entity]);

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
        response.Message.Should().Be("Communication channel updated successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(entity.Id);
        response.Data.Name.Should().Be("WhatsApp");
        response.Data.Provider.Should().Be("Twilio");
        response.Data.Code.Should().Be("WA-001");
        response.Data.IsActive.Should().BeTrue();

        entity.Name.Should().Be("WhatsApp");
        entity.Provider.Should().Be("Twilio");
        entity.Code.Should().Be("WA-001");
        entity.IsActive.Should().BeTrue();

        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid command instance for the communication channel update flow.
    /// </summary>
    private static UpdateCommunicationChannelCommand CreateValidCommand(Guid id)
    {
        return new UpdateCommunicationChannelCommand
        {
            Id = id,
            Name = "  WhatsApp  ",
            Provider = "  Twilio  ",
            Code = "  WA-001  ",
            IsActive = true
        };
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
    /// Holds the reusable mocks and helper factory for the update handler.
    /// </summary>
    private sealed class UpdateCommunicationChannelCommandTestContext
    {
        public UpdateCommunicationChannelCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CommunicationChannel>> RepositoryMock { get; } = new();

        public UpdateCommunicationChannelCommandHandler CreateHandler()
        {
            return new UpdateCommunicationChannelCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
