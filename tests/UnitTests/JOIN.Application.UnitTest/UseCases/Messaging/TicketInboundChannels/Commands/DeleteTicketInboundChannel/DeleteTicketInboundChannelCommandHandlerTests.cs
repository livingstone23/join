using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Commands.DeleteTicketInboundChannel;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;
using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketInboundChannels.Commands.DeleteTicketInboundChannel;

/// <summary> SPEC 47 F9 — <see cref="DeleteTicketInboundChannelCommandHandler"/>. </summary>
public sealed class DeleteTicketInboundChannelCommandHandlerTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly TicketInboundChannel _row;

    public DeleteTicketInboundChannelCommandHandlerTests()
    {
        _row = new TicketInboundChannel { CompanyId = _companyId, ChannelId = Guid.NewGuid(), WebhookToken = "token" };
        _currentUser.SetupGet(x => x.CompanyId).Returns(_companyId);
        _unitOfWork.SetupRepositoryRows(new[] { _row });
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private DeleteTicketInboundChannelCommandHandler Handler() => new(_unitOfWork.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_WhenCompanyIsMissing_ShouldReturnCompanyRequired()
    {
        _currentUser.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var response = await Handler().Handle(new DeleteTicketInboundChannelCommand(_row.Id), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenRowIsUnknownOrOfAnotherCompany_ShouldReturnNotFound()
    {
        var unknown = await Handler().Handle(new DeleteTicketInboundChannelCommand(Guid.NewGuid()), CancellationToken.None);
        _currentUser.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        var foreign = await Handler().Handle(new DeleteTicketInboundChannelCommand(_row.Id), CancellationToken.None);

        unknown.Message.Should().Be(TicketInboundErrorCodes.NotFound);
        foreign.Message.Should().Be(TicketInboundErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_HappyPath_ShouldSoftDeleteTheRow()
    {
        var response = await Handler().Handle(new DeleteTicketInboundChannelCommand(_row.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        _row.GcRecord.Should().NotBe(BaseAuditableEntity.ActiveGcRecord);
    }

    [Fact]
    public async Task Handle_WhenNothingIsSaved_ShouldReturnDeleteFailed()
    {
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var response = await Handler().Handle(new DeleteTicketInboundChannelCommand(_row.Id), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
    }
}
