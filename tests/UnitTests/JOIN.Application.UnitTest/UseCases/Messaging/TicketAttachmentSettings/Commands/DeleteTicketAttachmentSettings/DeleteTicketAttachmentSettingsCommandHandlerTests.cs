using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;
using MediatR;
using Moq;
using TicketAttachmentSettingsEntity = JOIN.Domain.Messaging.TicketAttachmentSettings;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketAttachmentSettings.Commands.DeleteTicketAttachmentSettings;

/// <summary>
/// Unit tests for <see cref="DeleteTicketAttachmentSettingsCommandHandler"/>.
/// </summary>
public sealed class DeleteTicketAttachmentSettingsCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new DeleteTicketAttachmentSettingsTestContext(Guid.Empty);
        var handler = context.CreateHandler();
        var id = _fixture.Create<Guid>();

        var response = await handler.Handle(new DeleteTicketAttachmentSettingsCommand(id), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.RepositoryMock.Verify(x => x.GetAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenConfigurationDoesNotExist_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteTicketAttachmentSettingsTestContext(companyId);
        var handler = context.CreateHandler();
        var id = _fixture.Create<Guid>();

        context.RepositoryMock.Setup(x => x.GetAsync(id)).ReturnsAsync((TicketAttachmentSettingsEntity?)null);

        var response = await handler.Handle(new DeleteTicketAttachmentSettingsCommand(id), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_ATTACHMENT_SETTINGS_NOT_FOUND");
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenConfigurationBelongsToDifferentTenant_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteTicketAttachmentSettingsTestContext(companyId);
        var handler = context.CreateHandler();
        var id = _fixture.Create<Guid>();

        context.RepositoryMock.Setup(x => x.GetAsync(id))
            .ReturnsAsync(new TicketAttachmentSettingsEntity { CompanyId = _fixture.Create<Guid>(), GcRecord = 0 });

        var response = await handler.Handle(new DeleteTicketAttachmentSettingsCommand(id), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_ATTACHMENT_SETTINGS_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenConfigurationIsActiveInTenant_ShouldMarkAsDeletedAndReturnSuccess()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteTicketAttachmentSettingsTestContext(companyId);
        var handler = context.CreateHandler();
        var id = _fixture.Create<Guid>();

        var entity = new TicketAttachmentSettingsEntity { CompanyId = companyId, GcRecord = 0 };
        SetEntityId(entity, id);
        context.RepositoryMock.Setup(x => x.GetAsync(id)).ReturnsAsync(entity);
        context.RepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TicketAttachmentSettingsEntity>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await handler.Handle(new DeleteTicketAttachmentSettingsCommand(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(id);
        entity.GcRecord.Should().NotBe(0); // soft-deleted stamp applied by MarkAsDeleted
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteTicketAttachmentSettingsTestContext(companyId);
        var handler = context.CreateHandler();
        var id = _fixture.Create<Guid>();

        context.RepositoryMock.Setup(x => x.GetAsync(id))
            .ReturnsAsync(new TicketAttachmentSettingsEntity { CompanyId = companyId, GcRecord = 0 });
        context.RepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TicketAttachmentSettingsEntity>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var response = await handler.Handle(new DeleteTicketAttachmentSettingsCommand(id), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
    }

    private sealed class DeleteTicketAttachmentSettingsTestContext
    {
        public DeleteTicketAttachmentSettingsTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            UnitOfWorkMock.Setup(x => x.GetRepository<TicketAttachmentSettingsEntity>()).Returns(RepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketAttachmentSettingsEntity>> RepositoryMock { get; } = new();

        public IRequestHandler<DeleteTicketAttachmentSettingsCommand, Response<Guid>> CreateHandler()
            => new DeleteTicketAttachmentSettingsCommandHandler(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }

    private static void SetEntityId(JOIN.Domain.Audit.BaseEntity entity, Guid id)
        => typeof(JOIN.Domain.Audit.BaseEntity).GetProperty(nameof(JOIN.Domain.Audit.BaseEntity.Id))!.SetValue(entity, id);
}