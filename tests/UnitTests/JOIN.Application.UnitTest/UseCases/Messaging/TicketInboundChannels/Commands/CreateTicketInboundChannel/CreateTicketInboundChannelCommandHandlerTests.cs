using System.Text.RegularExpressions;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Commands.CreateTicketInboundChannel;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketInboundChannels.Commands.CreateTicketInboundChannel;

/// <summary>
/// SPEC 47 F9 — unit tests for <see cref="CreateTicketInboundChannelCommandHandler"/>.
/// Covers the seven branches listed in the spec: tenant gate, supported
/// channels, unsupported channel, duplicate-active guard, single insert/save,
/// and token uniqueness.
/// </summary>
public sealed class CreateTicketInboundChannelCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary> Alphabet that confirms the returned token is Base64Url-encoded. </summary>
    private static readonly Regex Base64UrlRegex = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    /// <summary>
    /// Tenant gate: with no CompanyId on the token the handler short-circuits
    /// before touching any repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new CreateTicketInboundChannelTestContext();
        var handler = context.CreateHandler();
        var request = CreateValidCommand();

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.InboundChannelRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketInboundChannel>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// WHATSAPP is a supported inbound channel; the handler issues an opaque
    /// webhook token visible only in this create response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenChannelIsWhatsApp_ShouldGenerateBase64UrlToken()
    {
        var context = new CreateTicketInboundChannelTestContext();
        var request = CreateValidCommand();
        context.SetActiveTenant(request.CompanyId);
        context.SetSupportedChannel(request.ChannelId, CommunicationChannelCodes.WhatsApp, "WhatsApp Business");

        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.WebhookToken.Should().NotBeNullOrEmpty();
        response.Data.WebhookToken.Length.Should().BeGreaterThanOrEqualTo(43);
        Base64UrlRegex.IsMatch(response.Data.WebhookToken).Should().BeTrue();
        response.Data.ChannelCode.Should().Be(CommunicationChannelCodes.WhatsApp);
        response.Data.ChannelName.Should().Be("WhatsApp Business");
    }

    /// <summary>
    /// SENDGRID is the second supported inbound channel (e-mail ingestion).
    /// </summary>
    [Fact]
    public async Task Handle_WhenChannelIsSendGrid_ShouldGenerateBase64UrlToken()
    {
        var context = new CreateTicketInboundChannelTestContext();
        var request = CreateValidCommand();
        context.SetActiveTenant(request.CompanyId);
        context.SetSupportedChannel(request.ChannelId, CommunicationChannelCodes.SendGrid, "SendGrid Mail");

        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.ChannelCode.Should().Be(CommunicationChannelCodes.SendGrid);
        response.Data.WebhookToken.Length.Should().BeGreaterThanOrEqualTo(43);
    }

    /// <summary>
    /// Only WHATSAPP/SENDGRID are supported for inbound ingestion; the WEB
    /// channel (used by the in-app create path) must be rejected.
    /// </summary>
    [Fact]
    public async Task Handle_WhenChannelIsUnsupported_ShouldReturnUnsupportedChannelError()
    {
        var context = new CreateTicketInboundChannelTestContext();
        var request = CreateValidCommand();
        context.SetActiveTenant(request.CompanyId);
        context.SetSupportedChannel(request.ChannelId, CommunicationChannelCodes.Web, "Portal Web");

        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(TicketInboundErrorCodes.UnsupportedChannel);
        context.InboundChannelRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketInboundChannel>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// When an active row already exists for the (CompanyId, ChannelId) pair
    /// the handler refuses to issue a second token — rotating means delete
    /// + create, never in-place update (per SPEC 47 F2).
    /// </summary>
    [Fact]
    public async Task Handle_WhenActiveChannelAlreadyExists_ShouldReturnAlreadyActiveError()
    {
        var context = new CreateTicketInboundChannelTestContext();
        var request = CreateValidCommand();
        context.SetActiveTenant(request.CompanyId);
        context.SetSupportedChannel(request.ChannelId, CommunicationChannelCodes.WhatsApp, "WhatsApp");

        var existingId = Guid.NewGuid();
        var existing = new TicketInboundChannel
        {
            Id = existingId,
            CompanyId = request.CompanyId,
            ChannelId = request.ChannelId,
            WebhookToken = "oldtoken",
            GcRecord = BaseAuditableEntity.ActiveGcRecord
        };
        EntityIdSetter.SetId(existing, existingId);
        context.InboundChannelRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { existing });

        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INBOUND_CHANNEL_ALREADY_ACTIVE");
        context.InboundChannelRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketInboundChannel>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Successful create calls InsertAsync exactly once and SaveChangesAsync
    /// exactly once. Any leak here (e.g. two SaveChanges) means the transaction
    /// boundary isn't honored.
    /// </summary>
    [Fact]
    public async Task Handle_WhenHappyPath_ShouldInsertOnceAndSaveOnce()
    {
        var context = new CreateTicketInboundChannelTestContext();
        var request = CreateValidCommand();
        context.SetActiveTenant(request.CompanyId);
        context.SetSupportedChannel(request.ChannelId, CommunicationChannelCodes.WhatsApp, "WhatsApp");

        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.InboundChannelRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketInboundChannel>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Two consecutive successful creates must issue different tokens — the
    /// generator is random, so any duplication here would mean the RNG broke.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCalledRepeatedly_ShouldIssueDifferentTokens()
    {
        var context = new CreateTicketInboundChannelTestContext();
        var request = CreateValidCommand();
        context.SetActiveTenant(request.CompanyId);
        context.SetSupportedChannel(request.ChannelId, CommunicationChannelCodes.WhatsApp, "WhatsApp");

        var handler = context.CreateHandler();
        var first = await handler.Handle(request, CancellationToken.None);
        var second = await handler.Handle(request, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Data!.WebhookToken.Should().NotBe(second.Data!.WebhookToken);
    }

    private CreateTicketInboundChannelCommand CreateValidCommand() => new()
    {
        ChannelId = _fixture.Create<Guid>(),
        Description = "support inbox"
    };

    /// <summary>
    /// Holds the reusable mocks for the create handler tests.
    /// </summary>
    private sealed class CreateTicketInboundChannelTestContext
    {
        public CreateTicketInboundChannelTestContext()
        {
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            UnitOfWorkMock.Setup(x => x.GetRepository<TicketInboundChannel>())
                .Returns(InboundChannelRepositoryMock.Object);
            InboundChannelRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketInboundChannel>())).ReturnsAsync(true);
            InboundChannelRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketInboundChannel>());

            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketInboundChannel>> InboundChannelRepositoryMock { get; } = new();

        public CreateTicketInboundChannelCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);

        /// <summary>
        /// Aligns the mocked token tenant with the request's CompanyId so the
        /// handler's tenant gate passes.
        /// </summary>
        public void SetActiveTenant(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        }

        /// <summary>
        /// Registers the CommunicationChannel repository with an active,
        /// supported channel so the handler passes both the existence and
        /// the "supported Code" branches.
        /// </summary>
        public void SetSupportedChannel(Guid channelId, string code, string name)
        {
            var channelRepositoryMock = new Mock<IGenericRepository<CommunicationChannel>>();
            channelRepositoryMock
                .Setup(x => x.GetAsync(channelId))
                .ReturnsAsync(new CommunicationChannel
                {
                    Name = name,
                    Code = code,
                    IsActive = true,
                    GcRecord = BaseAuditableEntity.ActiveGcRecord
                });
            UnitOfWorkMock.Setup(x => x.GetRepository<CommunicationChannel>())
                .Returns(channelRepositoryMock.Object);
        }
    }
}
