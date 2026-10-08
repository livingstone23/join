using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;
using Moq;
using TicketAttachmentSettingsEntity = JOIN.Domain.Messaging.TicketAttachmentSettings;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketAttachmentSettings.Commands.RestoreTicketAttachmentSettings;

/// <summary>
/// Unit tests for <see cref="RestoreTicketAttachmentSettingsCommandHandler"/> (SPEC 41, Etapa 4).
/// </summary>
public sealed class RestoreTicketAttachmentSettingsCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(companyId: Guid.Empty);

        var response = await context.HandleAsync(Guid.NewGuid());

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotSuperAdmin_ShouldReturnSuperAdminRequired()
    {
        var context = new TestContext(isSuperAdmin: false);
        var entity = context.SetupDeleted();

        var response = await context.HandleAsync(entity.Id);

        response.Message.Should().Be("SUPERADMIN_REQUIRED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenEntityBelongsToAnotherCompany_ShouldReturnNotFound()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted(companyId: Guid.NewGuid());

        var response = await context.HandleAsync(entity.Id);

        response.Message.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenChecksPass_ShouldRestore()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.HandleAsync(entity.Id);

        response.IsSuccess.Should().BeTrue();
        entity.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenActiveDuplicateExists_ShouldReturnActiveDuplicateExists()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.EntityRepositoryMock.SetupRows([entity, context.ActiveTwinOf(entity)]);

        var response = await context.HandleAsync(entity.Id);

        response.Message.Should().Be("ACTIVE_DUPLICATE_EXISTS");
        entity.IsDeleted.Should().BeTrue();
    }

    private sealed class TestContext
    {
        public TestContext(Guid? companyId = null, bool isSuperAdmin = true)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId ?? CompanyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketAttachmentSettingsEntity>()).Returns(EntityRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketAttachmentSettingsEntity>> EntityRepositoryMock { get; } = new();

        public TicketAttachmentSettingsEntity SetupDeleted(Guid? companyId = null)
        {
            var entity = new TicketAttachmentSettingsEntity { CompanyId = companyId ?? CompanyId, MaxFilesPerTicket = 5, MaxFileSizeBytes = 1024 };
            entity.MarkAsDeleted();
            EntityRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(entity.Id)).ReturnsAsync(entity);
            EntityRepositoryMock.SetupRows([entity]);
            return entity;
        }

        public TicketAttachmentSettingsEntity ActiveTwinOf(TicketAttachmentSettingsEntity entity) => new TicketAttachmentSettingsEntity { CompanyId = entity.CompanyId, MaxFilesPerTicket = 1, MaxFileSizeBytes = 1 };

        public Task<Response<Guid>> HandleAsync(Guid id)
            => new RestoreTicketAttachmentSettingsCommandHandler(CurrentUserServiceMock.Object, new SoftDeleteRestorer(UnitOfWorkMock.Object, CurrentUserServiceMock.Object))
                .Handle(new RestoreTicketAttachmentSettingsCommand(id), CancellationToken.None);
    }
}
