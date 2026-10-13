using FluentAssertions;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Queries.ResolveTicketInboundChannel;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketInboundChannels.Queries.ResolveTicketInboundChannel;

/// <summary> SPEC 47 F9 — <see cref="ResolveTicketInboundChannelQueryHandler"/>: the anonymous token lookup. </summary>
public sealed class ResolveTicketInboundChannelQueryHandlerTests
{
    private readonly FakeSqlConnectionFactory _sql = new();

    [Theory]
    [InlineData("", "WHATSAPP")]
    [InlineData("token", " ")]
    public async Task Handle_WhenTokenOrCodeIsBlank_ShouldReturnNotFoundWithoutQuerying(string token, string code)
    {
        var response = await new ResolveTicketInboundChannelQueryHandler(_sql).Handle(new ResolveTicketInboundChannelQuery(token, code), CancellationToken.None);

        response.Message.Should().Be(TicketInboundErrorCodes.InboundChannelNotFound);
        _sql.LastCommandText.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenNoActiveRowMatches_ShouldReturnNotFound()
    {
        _sql.SetResults(FakeResultSet.Empty("Id", "CompanyId", "ChannelId", "ChannelCode"));

        var response = await new ResolveTicketInboundChannelQueryHandler(_sql).Handle(new ResolveTicketInboundChannelQuery("token", "WHATSAPP"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(TicketInboundErrorCodes.InboundChannelNotFound);
    }

    [Fact]
    public async Task Handle_WhenTheTokenMatches_ShouldReturnTenantAndChannelFilteringByTokenCodeAndSoftDelete()
    {
        var companyId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        _sql.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(),
            ["CompanyId"] = companyId,
            ["ChannelId"] = channelId,
            ["ChannelCode"] = "WHATSAPP"
        }));

        var response = await new ResolveTicketInboundChannelQueryHandler(_sql).Handle(new ResolveTicketInboundChannelQuery(" token ", "WHATSAPP"), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.CompanyId.Should().Be(companyId);
        response.Data.ChannelId.Should().Be(channelId);
        _sql.CapturedParameters["WebhookToken"].Should().Be("token");
        _sql.LastCommandText.Should().Contain("tic.gcrecord = 0").And.Contain("UPPER(cc.code) = UPPER(@ExpectedChannelCode)");
    }
}
