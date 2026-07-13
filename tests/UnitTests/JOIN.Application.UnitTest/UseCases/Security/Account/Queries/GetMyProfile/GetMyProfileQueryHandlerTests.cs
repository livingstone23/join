using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Security.Account.Queries.GetMyProfile;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Queries.GetMyProfile;

/// <summary>
/// Contains the unit tests for the authenticated user profile query handler.
/// </summary>
public sealed class GetMyProfileQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the user does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ShouldReturnAccountNotFoundError()
    {
        // Arrange
        var userId = _fixture.Create<Guid>();
        var context = new GetMyProfileQueryHandlerTestContext();

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetMyProfileQuery(userId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ACCOUNT_NOT_FOUND");
        response.Errors.Should().Contain("Authenticated account was not found.");
    }

    /// <summary>
    /// Verifies the not-found branch when the user account is inactive.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIsInactive_ShouldReturnAccountNotFoundError()
    {
        // Arrange
        var user = CreateUser(isActive: false);
        var context = new GetMyProfileQueryHandlerTestContext();

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetMyProfileQuery(user.Id), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ACCOUNT_NOT_FOUND");
    }

    /// <summary>
    /// Verifies the happy path when the user exists and communication channels are loaded via SQL.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserExists_ShouldReturnProfileWithCommunicationChannels()
    {
        // Arrange
        var user = CreateUser();
        var context = new GetMyProfileQueryHandlerTestContext();

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Type"] = "WhatsApp",
                    ["Value"] = "+34123456789",
                    ["IsPreferred"] = true
                },
                new Dictionary<string, object?>
                {
                    ["Type"] = "Telegram",
                    ["Value"] = "@joinuser",
                    ["IsPreferred"] = false
                }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetMyProfileQuery(user.Id), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Profile retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.UserName.Should().Be(user.UserName);
        response.Data.FirstName.Should().Be(user.FirstName);
        response.Data.LastName.Should().Be(user.LastName);
        response.Data.Email.Should().Be(user.Email);
        response.Data.CommunicationChannels.Should().HaveCount(2);
        response.Data.CommunicationChannels.First().Type.Should().Be("WhatsApp");
        response.Data.CommunicationChannels.First().Value.Should().Be("+34123456789");
        response.Data.CommunicationChannels.First().IsPreferred.Should().BeTrue();

        context.Connection.LastCommandText.Should().Contain("INNER JOIN Common.CommunicationChannels");
        context.Connection.LastCommandText.Should().Contain("WHERE ucc.UserId = @UserId");
        context.Connection.CapturedParameters["UserId"].Should().Be(user.Id);
    }

    private static ApplicationUser CreateUser(bool isActive = true)
    {
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "join.user",
            FirstName = "John",
            LastName = "Doe",
            Email = "user@join.com",
            PhoneNumber = "+34111222333",
            EmailConfirmed = true,
            PhoneNumberConfirmed = false,
            IsMfaEnabled = true,
            IsSuperAdmin = false,
            IsSuperAdminCompany = true,
            Created = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            IsActive = isActive,
            GcRecord = 0
        };
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();

        return new Mock<UserManager<ApplicationUser>>(
            store.Object,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
    }

    private sealed class GetMyProfileQueryHandlerTestContext
    {
        public GetMyProfileQueryHandlerTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } = CreateUserManagerMock();
        public FakeDbConnection Connection { get; } = new();

        public GetMyProfileQueryHandler CreateHandler()
        {
            return new GetMyProfileQueryHandler(UserManagerMock.Object, ConnectionFactoryMock.Object);
        }
    }
}
