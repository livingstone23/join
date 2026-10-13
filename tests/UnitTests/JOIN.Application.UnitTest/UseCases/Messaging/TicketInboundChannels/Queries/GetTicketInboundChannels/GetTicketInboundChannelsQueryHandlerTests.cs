using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Queries.GetTicketInboundChannels;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketInboundChannels.Queries.GetTicketInboundChannels;

/// <summary> SPEC 47 F9 — <see cref="GetTicketInboundChannelsQueryHandler"/>: tenant-scoped and always masked. </summary>
public sealed class GetTicketInboundChannelsQueryHandlerTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly FakeSqlConnectionFactory _sql = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    public GetTicketInboundChannelsQueryHandlerTests() => _currentUser.SetupGet(x => x.CompanyId).Returns(_companyId);

    [Fact]
    public async Task Handle_WhenCompanyIsMissing_ShouldReturnCompanyRequired()
    {
        _currentUser.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var response = await new GetTicketInboundChannelsQueryHandler(_sql, _currentUser.Object).Handle(new GetTicketInboundChannelsQuery(), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_ShouldFilterByTenantAndActiveRowsAndMaskTheToken()
    {
        _sql.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(),
            ["CompanyId"] = _companyId,
            ["ChannelId"] = Guid.NewGuid(),
            ["ChannelCode"] = "WHATSAPP",
            ["ChannelName"] = "WhatsApp",
            ["Description"] = null,
            ["WebhookToken"] = "abcdefghijklmnopqrstuvwxyz1a2b",
            ["CreatedAt"] = DateTime.UtcNow
        }));

        var response = await new GetTicketInboundChannelsQueryHandler(_sql, _currentUser.Object).Handle(new GetTicketInboundChannelsQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Single().WebhookTokenMasked.Should().Be("••••••••1a2b");
        _sql.LastCommandText.Should().Contain("tic.companyid = @TenantId").And.Contain("tic.gcrecord = 0");
        _sql.CapturedParameters["TenantId"].Should().Be(_companyId);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("abc", "•••")]
    public void MaskToken_ShouldNeverRevealShortOrMissingTokens(string? token, string expected) =>
        GetTicketInboundChannelsQueryHandler.MaskToken(token).Should().Be(expected);
}
