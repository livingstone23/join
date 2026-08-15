using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Account.Queries.GetSecurityActivity;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Queries.GetSecurityActivity;

/// <summary>
/// Unit tests for the <c>GET /api/v1/account/security-activity</c> handler
/// (SPEC 26 / F9).
/// </summary>
public sealed class GetSecurityActivityQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Paged happy path: rows are mapped to DTOs (enum names) and the totals reflect the
    /// repository's count call. Items come back ordered by <c>OccurredAtUtc DESC</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRepositoryReturnsPagedRows_ShouldMapAndReturnPagedResult()
    {
        // Arrange
        var context = new GetSecurityActivityQueryHandlerTestContext();
        var callerId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        var now = DateTime.UtcNow;
        var rows = new List<SecurityEventLog>
        {
            new SecurityEventLog(_fixture.Create<Guid>())
            {
                UserId = callerId,
                EventType = (int)SecurityEventType.LoginSucceeded,
                Result = (int)SecurityEventResult.Success,
                OccurredAtUtc = now.AddMinutes(-5),
                IpAddress = "10.0.0.1",
                UserAgent = "Mozilla/5.0"
            },
            new SecurityEventLog(_fixture.Create<Guid>())
            {
                UserId = callerId,
                EventType = (int)SecurityEventType.SessionRevoked,
                Result = (int)SecurityEventResult.Success,
                OccurredAtUtc = now.AddMinutes(-30),
                IpAddress = "10.0.0.2",
                UserAgent = "Chrome/127"
            }
        };

        context.SecurityEventRepositoryMock
            .Setup(x => x.ListByUserPagedAsync(callerId, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        context.SecurityEventRepositoryMock
            .Setup(x => x.CountByUserAsync(callerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows.Count);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetSecurityActivityQuery(1, 20), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().HaveCount(2);
        response.Data.TotalCount.Should().Be(2);
        response.Data.PageNumber.Should().Be(1);
        response.Data.PageSize.Should().Be(20);
        var items = response.Data.Items.ToList();
        items[0].Event.Should().Be(nameof(SecurityEventType.LoginSucceeded));
        items[0].Result.Should().Be(nameof(SecurityEventResult.Success));
        items[0].IpAddress.Should().Be("10.0.0.1");
        items[0].Device.Should().Be("Mozilla/5.0");
        items[1].Event.Should().Be(nameof(SecurityEventType.SessionRevoked));
    }

    /// <summary>
    /// pageNumber=0 clamps up to 1 before the repository is called.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPageNumberIsZero_ShouldClampToOne()
    {
        // Arrange
        var context = new GetSecurityActivityQueryHandlerTestContext();
        var callerId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        context.SecurityEventRepositoryMock
            .Setup(x => x.ListByUserPagedAsync(callerId, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SecurityEventLog>());
        context.SecurityEventRepositoryMock
            .Setup(x => x.CountByUserAsync(callerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetSecurityActivityQuery(0, 20), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data!.PageNumber.Should().Be(1);
        context.SecurityEventRepositoryMock.Verify(
            x => x.ListByUserPagedAsync(callerId, 1, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// pageSize=200 clamps down to 100 before the repository is called.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPageSizeExceedsMax_ShouldClampTo100()
    {
        // Arrange
        var context = new GetSecurityActivityQueryHandlerTestContext();
        var callerId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        context.SecurityEventRepositoryMock
            .Setup(x => x.ListByUserPagedAsync(callerId, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SecurityEventLog>());
        context.SecurityEventRepositoryMock
            .Setup(x => x.CountByUserAsync(callerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetSecurityActivityQuery(1, 200), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data!.PageSize.Should().Be(100);
        context.SecurityEventRepositoryMock.Verify(
            x => x.ListByUserPagedAsync(callerId, 1, 100, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Self-contained mocks for GetSecurityActivity dependencies.
    /// </summary>
    private sealed class GetSecurityActivityQueryHandlerTestContext
    {
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ISecurityEventRepository> SecurityEventRepositoryMock { get; } = new();

        public void SetUser(Guid userId)
        {
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());
        }

        public GetSecurityActivityQueryHandler CreateHandler() =>
            new(CurrentUserServiceMock.Object, SecurityEventRepositoryMock.Object);
    }
}
