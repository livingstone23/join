using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Queries.GetTicketInboundChannels;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketInboundChannels.Queries.GetTicketInboundChannels;

/// <summary>
/// SPEC 47 F9 — unit tests for <see cref="GetTicketInboundChannelsQueryHandler"/>.
/// Verifies the tenant gate, the never-clear-token-on-read invariant, and the
/// empty-result branch.
/// </summary>
public sealed class GetTicketInboundChannelsQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Tenant gate: with no CompanyId on the token the handler short-circuits
    /// before touching the connection.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new GetTicketInboundChannelsTestContext();
        var query = new GetTicketInboundChannelsQuery();

        var response = await context.CreateHandler().Handle(query, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    /// <summary>
    /// The mask-rendering is the only path to read the token after creation —
    /// the clear token must never appear in the read response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRowsExist_ShouldReturnMaskedTokensNotCleartext()
    {
        var context = new GetTicketInboundChannelsTestContext();
        var companyId = _fixture.Create<Guid>();
        context.SetActiveTenant(companyId);

        var clearToken = "abcdef1234567890abcdef1234567890abcdef12345678"; // > 4 chars to trigger last-4 mask
        context.ConnectionFactory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CompanyId"] = companyId,
                ["ChannelId"] = Guid.NewGuid(),
                ["ChannelCode"] = "WHATSAPP",
                ["ChannelName"] = "WhatsApp",
                ["Description"] = "support",
                ["WebhookToken"] = clearToken,
                ["CreatedAt"] = DateTime.UtcNow
            }));

        var response = await context.CreateHandler()
            .Handle(new GetTicketInboundChannelsQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull().And.HaveCount(1);
        var dto = response.Data!.Single();
        dto.WebhookTokenMasked.Should().NotBe(clearToken);
        dto.WebhookTokenMasked.Should().NotContain("abcdef1234567890abcdef1234567890abcdef12345"); // substring of cleartext must not leak
        dto.WebhookTokenMasked.Should().EndWith(clearToken[^4..]);
    }

    /// <summary>
    /// An empty result set is still a successful response (the listing is
    /// zero rows, not an error).
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoChannels_ShouldReturnEmptyList()
    {
        var context = new GetTicketInboundChannelsTestContext();
        context.SetActiveTenant(_fixture.Create<Guid>());
        context.ConnectionFactory.SetResults(
            FakeResultSet.Empty(
                "Id", "CompanyId", "ChannelId", "ChannelCode", "ChannelName",
                "Description", "WebhookToken", "CreatedAt"));

        var response = await context.CreateHandler()
            .Handle(new GetTicketInboundChannelsQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull().And.BeEmpty();
    }

    /// <summary>
    /// Holds the reusable mocks for the query handler tests.
    /// </summary>
    private sealed class GetTicketInboundChannelsTestContext
    {
        public GetTicketInboundChannelsTestContext()
        {
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            CurrentUserServiceMock.Setup(x => x.IsInRole(It.IsAny<string>())).Returns(false);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(ConnectionFactory);
        }

        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeSqlConnectionFactory ConnectionFactory { get; } = new();

        public GetTicketInboundChannelsQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);

        /// <summary>
        /// Aligns the mocked token tenant so the handler's tenant gate passes.
        /// </summary>
        public void SetActiveTenant(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        }
    }
}
