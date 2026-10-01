using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;
using MediatR;
using Moq;
using TicketAttachmentSettingsEntity = JOIN.Domain.Messaging.TicketAttachmentSettings;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketAttachmentSettings.Commands.CreateTicketAttachmentSettings;

/// <summary>
/// Unit tests for <see cref="CreateTicketAttachmentSettingsCommandHandler"/>.
/// Covers tenant gate, duplicate-active prevention, and the happy path.
/// </summary>
public sealed class CreateTicketAttachmentSettingsCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant claim is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new CreateTicketAttachmentSettingsTestContext(companyId: Guid.Empty);
        var handler = context.CreateHandler();
        var request = CreateValidCommand();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.RepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketAttachmentSettingsEntity>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate-active prevention: a second <c>Create</c> for
    /// a tenant that already has an active row returns
    /// <c>CONFIG_ALREADY_EXISTS</c> (409) without persisting.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActiveConfigurationAlreadyExists_ShouldReturnConfigAlreadyExistsError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketAttachmentSettingsTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { new TicketAttachmentSettingsEntity { CompanyId = companyId, GcRecord = 0 } });

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CONFIG_ALREADY_EXISTS");
        context.RepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketAttachmentSettingsEntity>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate check also ignores soft-deleted rows so a
    /// re-created configuration after a delete is allowed (the
    /// index-filter logic relies on this — see spec F2 decision).
    /// </summary>
    [Fact]
    public async Task Handle_WhenOnlySoftDeletedConfigurationExists_ShouldAllowCreate()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketAttachmentSettingsTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { new TicketAttachmentSettingsEntity { CompanyId = companyId, GcRecord = 1 } });

        var mappedEntity = new TicketAttachmentSettingsEntity();
        context.MapperMock.Setup(x => x.ToEntity(request)).Returns(mappedEntity);
        context.RepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<TicketAttachmentSettingsEntity>()))
            .ReturnsAsync(true);
        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        context.MapperMock.Verify(x => x.ToEntity(request), Times.Once);
        context.RepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketAttachmentSettingsEntity>()), Times.Once);
    }

    /// <summary>
    /// Verifies the happy path: the mapper is invoked, the entity is
    /// stamped with the current tenant, and the response carries the
    /// expected fields (the bitmask projects to the integer DTO field).
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldPersistEntityAndReturnSuccessResponse()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketAttachmentSettingsTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand();

        var entity = new TicketAttachmentSettingsEntity
        {
            AllowedDocumentTypes = (JOIN.Domain.Enums.DocumentType)request.AllowedDocumentTypes,
            MaxFileSizeBytes = request.MaxFileSizeBytes,
            MaxFilesPerTicket = request.MaxFilesPerTicket,
            MaxFilesPerDay = request.MaxFilesPerDay
        };

        context.MapperMock.Setup(x => x.ToEntity(request)).Returns(entity);
        context.RepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketAttachmentSettingsEntity>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket attachment settings created successfully.");
        response.Data!.CompanyId.Should().Be(companyId);
        response.Data.AllowedDocumentTypes.Should().Be(request.AllowedDocumentTypes);
        response.Data.MaxFileSizeBytes.Should().Be(request.MaxFileSizeBytes);
        response.Data.MaxFilesPerTicket.Should().Be(request.MaxFilesPerTicket);
        response.Data.MaxFilesPerDay.Should().Be(request.MaxFilesPerDay);
        response.Data.IsDeleted.Should().BeFalse();

        context.MapperMock.Verify(x => x.ToEntity(request), Times.Once);
        context.RepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketAttachmentSettingsEntity>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies the persistence-failure branch.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketAttachmentSettingsTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand();

        context.MapperMock.Setup(x => x.ToEntity(request)).Returns(new TicketAttachmentSettingsEntity());
        context.RepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketAttachmentSettingsEntity>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
    }

    private CreateTicketAttachmentSettingsCommand CreateValidCommand() => new()
    {
        AllowedDocumentTypes = (int)(JOIN.Domain.Enums.DocumentType.Pdf
            | JOIN.Domain.Enums.DocumentType.Word
            | JOIN.Domain.Enums.DocumentType.Image),
        MaxFileSizeBytes = 10L * 1024 * 1024,
        MaxFilesPerTicket = 10,
        MaxFilesPerDay = null
    };

    /// <summary>
    /// Holds the mocked UoW + current-user service for the create handler tests.
    /// The handler needs only the settings repository — no FK validations.
    /// </summary>
    private sealed class CreateTicketAttachmentSettingsTestContext
    {
        public CreateTicketAttachmentSettingsTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            UnitOfWorkMock.Setup(x => x.GetRepository<TicketAttachmentSettingsEntity>()).Returns(RepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ITicketAttachmentSettingsMapper> MapperMock { get; } = new();
        public Mock<IGenericRepository<TicketAttachmentSettingsEntity>> RepositoryMock { get; } = new();
        public IRequestHandler<CreateTicketAttachmentSettingsCommand, Response<JOIN.Application.DTO.Messaging.TicketAttachmentSettingsDto>> CreateHandler()
            => new CreateTicketAttachmentSettingsCommandHandler(UnitOfWorkMock.Object, CurrentUserServiceMock.Object, MapperMock.Object);
    }
}