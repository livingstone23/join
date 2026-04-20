using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.CommunicationChannels.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.CommunicationChannels.Commands.CreateCommunicationChannel;

/// <summary>
/// Contains the unit tests for the communication channel creation command handler.
/// These tests verify duplicate-name protection, persistence failures,
/// normalization, and the successful creation flow.
/// </summary>
public sealed class CreateCommunicationChannelCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameAlreadyExists_ShouldReturnNameInUseError()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateCommunicationChannelCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new CommunicationChannel
                {
                    Name = request.Name.Trim(),
                    IsActive = true,
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
        context.RepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CommunicationChannel>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateCommunicationChannelCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<CommunicationChannel>());

        context.RepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CommunicationChannel>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
        response.Errors.Should().Contain("No records were affected while creating the communication channel.");
    }

    /// <summary>
    /// Verifies the happy path and normalization behavior when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateCommunicationChannelAndReturnDto()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateCommunicationChannelCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<CommunicationChannel>());

        context.RepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CommunicationChannel>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Communication channel created successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Name.Should().Be("WhatsApp");
        response.Data.Provider.Should().Be("Twilio");
        response.Data.Code.Should().Be("WA-001");
        response.Data.IsActive.Should().BeTrue();

        context.RepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CommunicationChannel>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid command instance for the communication channel creation flow.
    /// </summary>
    private static CreateCommunicationChannelCommand CreateValidCommand()
    {
        return new CreateCommunicationChannelCommand
        {
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
    /// Holds the reusable mocks and helper factory for the create handler.
    /// </summary>
    private sealed class CreateCommunicationChannelCommandTestContext
    {
        public CreateCommunicationChannelCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CommunicationChannel>> RepositoryMock { get; } = new();

        public CreateCommunicationChannelCommandHandler CreateHandler()
        {
            return new CreateCommunicationChannelCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
