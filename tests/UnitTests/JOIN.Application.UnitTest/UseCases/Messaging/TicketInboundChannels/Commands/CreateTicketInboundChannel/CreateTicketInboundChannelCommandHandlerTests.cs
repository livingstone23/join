using System.Linq.Expressions;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Commands.CreateTicketInboundChannel;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketInboundChannels.Commands.CreateTicketInboundChannel;

/// <summary> SPEC 47 F9 — <see cref="CreateTicketInboundChannelCommandHandler"/>. </summary>
public sealed class CreateTicketInboundChannelCommandHandlerTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IDatabaseErrorClassifier> _errorClassifier = new();
    private readonly List<TicketInboundChannel> _inboundChannels = [];
    private readonly CommunicationChannel _whatsApp = new() { Name = "WhatsApp", Code = CommunicationChannelCodes.WhatsApp };

    public CreateTicketInboundChannelCommandHandlerTests()
    {
        _currentUser.SetupGet(x => x.CompanyId).Returns(_companyId);
        var channels = _unitOfWork.SetupRepositoryRows(new[] { _whatsApp });
        channels.Setup(x => x.GetAsync(_whatsApp.Id)).ReturnsAsync(_whatsApp);
        var inbound = new Mock<IGenericRepository<TicketInboundChannel>>();
        inbound
            .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<Expression<Func<TicketInboundChannel, bool>>>()))
            .ReturnsAsync((Expression<Func<TicketInboundChannel, bool>> predicate) => _inboundChannels.Where(predicate.Compile()).ToList());
        inbound.Setup(x => x.InsertAsync(It.IsAny<TicketInboundChannel>())).ReturnsAsync(true);
        _unitOfWork.Setup(x => x.GetRepository<TicketInboundChannel>()).Returns(inbound.Object);
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private CreateTicketInboundChannelCommandHandler Handler() => new(_unitOfWork.Object, _currentUser.Object, _errorClassifier.Object);

    [Fact]
    public async Task Handle_WhenCompanyIsMissing_ShouldReturnCompanyRequired()
    {
        _currentUser.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var response = await Handler().Handle(new CreateTicketInboundChannelCommand { ChannelId = _whatsApp.Id }, CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenChannelDoesNotExistOrIsInactive_ShouldReturnInvalidChannel()
    {
        var unknown = await Handler().Handle(new CreateTicketInboundChannelCommand { ChannelId = Guid.NewGuid() }, CancellationToken.None);
        _whatsApp.IsActive = false;
        var inactive = await Handler().Handle(new CreateTicketInboundChannelCommand { ChannelId = _whatsApp.Id }, CancellationToken.None);

        unknown.Message.Should().Be(TicketInboundErrorCodes.InvalidChannel);
        inactive.Message.Should().Be(TicketInboundErrorCodes.InvalidChannel);
    }

    [Fact]
    public async Task Handle_WhenChannelCodeHasNoWebhook_ShouldReturnUnsupportedInboundChannel()
    {
        _whatsApp.Code = CommunicationChannelCodes.Telegram;

        var response = await Handler().Handle(new CreateTicketInboundChannelCommand { ChannelId = _whatsApp.Id }, CancellationToken.None);

        response.Message.Should().Be(TicketInboundErrorCodes.UnsupportedChannel);
    }

    [Fact]
    public async Task Handle_WhenAnActiveRowAlreadyExists_ShouldReturnAlreadyActive()
    {
        _inboundChannels.Add(new TicketInboundChannel { CompanyId = _companyId, ChannelId = _whatsApp.Id, WebhookToken = "old" });

        var response = await Handler().Handle(new CreateTicketInboundChannelCommand { ChannelId = _whatsApp.Id }, CancellationToken.None);

        response.Message.Should().Be(TicketInboundErrorCodes.AlreadyActive);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAConcurrentCreateWinsTheIndex_ShouldReturnAlreadyActive()
    {
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("23505"));
        _errorClassifier.Setup(x => x.IsUniqueViolation(It.IsAny<Exception>(), TicketInboundErrorCodes.CompanyChannelIndex)).Returns(true);

        var response = await Handler().Handle(new CreateTicketInboundChannelCommand { ChannelId = _whatsApp.Id }, CancellationToken.None);

        response.Message.Should().Be(TicketInboundErrorCodes.AlreadyActive);
    }

    [Fact]
    public async Task Handle_HappyPath_ShouldReturnTheClearBase64UrlTokenOnce()
    {
        var response = await Handler().Handle(
            new CreateTicketInboundChannelCommand { ChannelId = _whatsApp.Id, Description = "  Soporte  " },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.WebhookToken.Should().MatchRegex("^[A-Za-z0-9_-]{43,}$");
        response.Data.ChannelCode.Should().Be(CommunicationChannelCodes.WhatsApp);
        response.Data.Description.Should().Be("Soporte");
        response.Data.CompanyId.Should().Be(_companyId);
    }

    [Fact]
    public void Validator_ShouldRequireChannelAndLimitDescription()
    {
        var validator = new CreateTicketInboundChannelCommandValidator();

        validator.Validate(new CreateTicketInboundChannelCommand()).IsValid.Should().BeFalse();
        validator.Validate(new CreateTicketInboundChannelCommand { ChannelId = Guid.NewGuid(), Description = new string('d', 501) }).IsValid.Should().BeFalse();
        validator.Validate(new CreateTicketInboundChannelCommand { ChannelId = Guid.NewGuid() }).IsValid.Should().BeTrue();
    }
}
