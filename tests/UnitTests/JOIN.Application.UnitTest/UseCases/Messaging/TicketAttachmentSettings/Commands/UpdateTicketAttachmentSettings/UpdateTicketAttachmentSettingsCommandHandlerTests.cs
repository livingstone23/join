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

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketAttachmentSettings.Commands.UpdateTicketAttachmentSettings;

/// <summary>
/// Unit tests for <see cref="UpdateTicketAttachmentSettingsCommandHandler"/>.
/// </summary>
public sealed class UpdateTicketAttachmentSettingsCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new UpdateTicketAttachmentSettingsTestContext(Guid.Empty);
        var handler = context.CreateHandler();
        var request = CreateValidCommand();

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.RepositoryMock.Verify(x => x.GetAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenConfigurationDoesNotExist_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new UpdateTicketAttachmentSettingsTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand();

        context.RepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync((TicketAttachmentSettingsEntity?)null);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_ATTACHMENT_SETTINGS_NOT_FOUND");
        context.MapperMock.Verify(x => x.ApplyUpdate(It.IsAny<UpdateTicketAttachmentSettingsCommand>(), It.IsAny<TicketAttachmentSettingsEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenConfigurationBelongsToDifferentTenant_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new UpdateTicketAttachmentSettingsTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand();

        context.RepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(new TicketAttachmentSettingsEntity { CompanyId = _fixture.Create<Guid>(), GcRecord = 0 });

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_ATTACHMENT_SETTINGS_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenConfigurationIsSoftDeleted_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new UpdateTicketAttachmentSettingsTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand();

        context.RepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(new TicketAttachmentSettingsEntity { CompanyId = companyId, GcRecord = 1 });

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_ATTACHMENT_SETTINGS_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenAllChecksPass_ShouldApplyUpdateAndReturnSuccessResponse()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new UpdateTicketAttachmentSettingsTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand();

        var entity = new TicketAttachmentSettingsEntity { CompanyId = companyId, GcRecord = 0 };
        context.RepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);
        context.RepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TicketAttachmentSettingsEntity>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.CompanyId.Should().Be(companyId);
        context.MapperMock.Verify(x => x.ApplyUpdate(request, entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new UpdateTicketAttachmentSettingsTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand();

        context.RepositoryMock.Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(new TicketAttachmentSettingsEntity { CompanyId = companyId, GcRecord = 0 });
        context.RepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TicketAttachmentSettingsEntity>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("UPDATE_FAILED");
    }

    private UpdateTicketAttachmentSettingsCommand CreateValidCommand() => new()
    {
        Id = _fixture.Create<Guid>(),
        AllowedDocumentTypes = 31,
        MaxFileSizeBytes = 5L * 1024 * 1024,
        MaxFilesPerTicket = 5,
        MaxFilesPerDay = 100
    };

    private sealed class UpdateTicketAttachmentSettingsTestContext
    {
        public UpdateTicketAttachmentSettingsTestContext(Guid companyId)
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

        public IRequestHandler<UpdateTicketAttachmentSettingsCommand, Response<JOIN.Application.DTO.Messaging.TicketAttachmentSettingsDto>> CreateHandler()
            => new UpdateTicketAttachmentSettingsCommandHandler(UnitOfWorkMock.Object, CurrentUserServiceMock.Object, MapperMock.Object);
    }
}