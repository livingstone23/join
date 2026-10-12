using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Commands.DeleteTicketInboundChannel;
using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketInboundChannels.Commands.DeleteTicketInboundChannel;

/// <summary>
/// SPEC 47 F9 — unit tests for <see cref="DeleteTicketInboundChannelCommandHandler"/>.
/// Soft-deletes the inbound channel row; once GcRecord flips, the webhook token
/// stops resolving and the next inbound POST receives 404.
/// </summary>
public sealed class DeleteTicketInboundChannelCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Tenant gate: with no CompanyId on the token the handler short-circuits
    /// before looking up the row.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new DeleteTicketInboundChannelTestContext();
        var request = new DeleteTicketInboundChannelCommand(_fixture.Create<Guid>());
        var handler = context.CreateHandler();

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.InboundChannelRepositoryMock.Verify(x => x.GetAsync(It.IsAny<Guid>()), Times.Never);
    }

    /// <summary>
    /// If the row is missing (already deleted, or wrong tenant) the handler
    /// must not flip GcRecord on any other entity.
    /// </summary>
    [Fact]
    public async Task Handle_WhenChannelNotFound_ShouldReturnChannelNotFoundError()
    {
        var context = new DeleteTicketInboundChannelTestContext();
        var request = new DeleteTicketInboundChannelCommand(_fixture.Create<Guid>());
        context.SetActiveTenant(_fixture.Create<Guid>());
        context.InboundChannelRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync((TicketInboundChannel?)null);

        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_INBOUND_CHANNEL_NOT_FOUND");
        context.InboundChannelRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TicketInboundChannel>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Happy path: the active row is marked as deleted, UpdateAsync and
    /// SaveChangesAsync fire, and the response echoes the deleted id.
    /// </summary>
    [Fact]
    public async Task Handle_WhenChannelExists_ShouldMarkDeletedAndReturnId()
    {
        var context = new DeleteTicketInboundChannelTestContext();
        var companyId = _fixture.Create<Guid>();
        var channelId = _fixture.Create<Guid>();
        var request = new DeleteTicketInboundChannelCommand(channelId);
        context.SetActiveTenant(companyId);

        var entity = new TicketInboundChannel
        {
            CompanyId = companyId,
            ChannelId = _fixture.Create<Guid>(),
            WebhookToken = "abc",
            GcRecord = BaseAuditableEntity.ActiveGcRecord
        };
        EntityIdSetter.SetId(entity, channelId);
        context.InboundChannelRepositoryMock.Setup(x => x.GetAsync(channelId)).ReturnsAsync(entity);

        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(channelId);
        entity.GcRecord.Should().NotBe(BaseAuditableEntity.ActiveGcRecord);
        context.InboundChannelRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Holds the reusable mocks for the delete handler tests.
    /// </summary>
    private sealed class DeleteTicketInboundChannelTestContext
    {
        public DeleteTicketInboundChannelTestContext()
        {
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            UnitOfWorkMock.Setup(x => x.GetRepository<TicketInboundChannel>())
                .Returns(InboundChannelRepositoryMock.Object);
            InboundChannelRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TicketInboundChannel>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketInboundChannel>> InboundChannelRepositoryMock { get; } = new();

        public DeleteTicketInboundChannelCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);

        /// <summary>
        /// Aligns the mocked token tenant with the request's CompanyId so the
        /// handler's tenant gate passes.
        /// </summary>
        public void SetActiveTenant(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        }
    }
}
