using AutoFixture;
using FluentAssertions;
using JOIN.Application.UseCases.Security.Users.Commands.ReplaceUserRoles;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Users.Commands.ReplaceUserRoles;

/// <summary>
/// Contains the unit tests for replacing the complete role set assigned to a user.
/// These tests verify user lookup, requested-role validation,
/// failure branches from Identity, and the successful replacement flow.
/// </summary>
public sealed class ReplaceUserRolesCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the requested user does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ShouldReturnUserNotFoundError()
    {
        // Arrange
        var context = new ReplaceUserRolesCommandTestContext();
        var request = new ReplaceUserRolesCommand(_fixture.Create<Guid>(), ["Admin"]);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(request.UserId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("User not found.");
    }

    /// <summary>
    /// Verifies the role-validation branch when any requested role does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnyRequestedRoleDoesNotExist_ShouldReturnRoleNotFoundError()
    {
        // Arrange
        var user = CreateUser();
        var context = new ReplaceUserRolesCommandTestContext();
        var request = new ReplaceUserRolesCommand(user.Id, ["Admin", "Ghost"]);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        context.SetAvailableRoles("Admin", "Manager");

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("One or more roles do not exist.");
        response.Errors.Should().Contain("Role 'Ghost' does not exist.");
    }

    /// <summary>
    /// Verifies the add-to-roles failure branch propagated from Identity.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAddToRolesFails_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var user = CreateUser();
        var context = new ReplaceUserRolesCommandTestContext();
        var request = new ReplaceUserRolesCommand(user.Id, ["Admin", "Manager"]);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        context.SetAvailableRoles("Admin", "Manager");
        context.AssignedRoles.Add("Viewer");

        context.UserManagerMock
            .Setup(x => x.AddToRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Unable to add roles." }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("Unable to add one or more roles.");
        response.Errors.Should().Contain("Unable to add roles.");
    }

    /// <summary>
    /// Verifies the remove-from-roles failure branch propagated from Identity.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRemoveFromRolesFails_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var user = CreateUser();
        var context = new ReplaceUserRolesCommandTestContext();
        var request = new ReplaceUserRolesCommand(user.Id, ["Admin"]);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        context.SetAvailableRoles("Admin", "Viewer");
        context.AssignedRoles.AddRange(["Admin", "Viewer"]);

        context.UserManagerMock
            .Setup(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Unable to remove roles." }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("Unable to remove one or more roles.");
        response.Errors.Should().Contain("Unable to remove roles.");
    }

    /// <summary>
    /// Verifies the successful replacement flow and the sorted role projection in the returned DTO.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldReplaceRolesAndReturnSortedDto()
    {
        // Arrange
        var user = CreateUser();
        var context = new ReplaceUserRolesCommandTestContext();
        var request = new ReplaceUserRolesCommand(user.Id, ["  manager  ", "Admin", "admin", "  "]);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        context.SetAvailableRoles("Admin", "manager", "Viewer");
        context.AssignedRoles.Add("Viewer");

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("User roles updated successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(user.Id);
        response.Data.UserName.Should().Be("jdoe");
        response.Data.Email.Should().Be("jdoe@joincrm.com");
        response.Data.Roles.Should().Equal("Admin", "manager");

        context.UserManagerMock.Verify(
            x => x.AddToRolesAsync(user, It.Is<IEnumerable<string>>(roles => roles.SequenceEqual(new[] { "Admin", "manager" }))),
            Times.Once);

        context.UserManagerMock.Verify(
            x => x.RemoveFromRolesAsync(user, It.Is<IEnumerable<string>>(roles => roles.SequenceEqual(new[] { "Viewer" }))),
            Times.Once);
    }

    /// <summary>
    /// Verifies the successful flow when the requested role set is empty.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestContainsNoRoles_ShouldRemoveExistingRolesAndReturnEmptyList()
    {
        // Arrange
        var user = CreateUser();
        var context = new ReplaceUserRolesCommandTestContext();
        var request = new ReplaceUserRolesCommand(user.Id, Array.Empty<string>());

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        context.SetAvailableRoles("Admin", "Viewer");
        context.AssignedRoles.AddRange(["Admin", "Viewer"]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Roles.Should().BeEmpty();

        context.UserManagerMock.Verify(
            x => x.AddToRolesAsync(user, It.IsAny<IEnumerable<string>>()),
            Times.Never);

        context.UserManagerMock.Verify(
            x => x.RemoveFromRolesAsync(user, It.Is<IEnumerable<string>>(roles => roles.OrderBy(role => role).SequenceEqual(new[] { "Admin", "Viewer" }))),
            Times.Once);
    }

    /// <summary>
    /// Creates a reusable user instance for the command scenarios.
    /// </summary>
    private ApplicationUser CreateUser()
    {
        return new ApplicationUser
        {
            Id = _fixture.Create<Guid>(),
            UserName = "jdoe",
            Email = "jdoe@joincrm.com",
            FirstName = "John",
            LastName = "Doe",
            IsActive = true
        };
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the replace-roles handler.
    /// </summary>
    private sealed class ReplaceUserRolesCommandTestContext
    {
        public ReplaceUserRolesCommandTestContext()
        {
            UserManagerMock = CreateUserManagerMock();
            RoleManagerMock = CreateRoleManagerMock();

            UserManagerMock
                .Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
                .ReturnsAsync(() => (IList<string>)AssignedRoles.OrderBy(role => role).ToList());

            UserManagerMock
                .Setup(x => x.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((ApplicationUser _, IEnumerable<string> roles) =>
                {
                    AssignedRoles.Clear();
                    AssignedRoles.AddRange(roles.Distinct(StringComparer.OrdinalIgnoreCase));
                    return IdentityResult.Success;
                });

            UserManagerMock
                .Setup(x => x.RemoveFromRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((ApplicationUser _, IEnumerable<string> roles) =>
                {
                    foreach (var role in roles.ToList())
                    {
                        AssignedRoles.RemoveAll(existing => string.Equals(existing, role, StringComparison.OrdinalIgnoreCase));
                    }

                    return IdentityResult.Success;
                });
        }

        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; }
        public Mock<RoleManager<ApplicationRole>> RoleManagerMock { get; }
        public List<string> AssignedRoles { get; } = new();
        private List<ApplicationRole> AvailableRoles { get; } = new();

        public void SetAvailableRoles(params string[] names)
        {
            AvailableRoles.Clear();
            AvailableRoles.AddRange(names.Select(name => new ApplicationRole
            {
                Name = name,
                NormalizedName = name.ToUpperInvariant()
            }));
            RoleManagerMock.SetupGet(x => x.Roles).Returns(AvailableRoles.AsQueryable());
        }

        public ReplaceUserRolesCommandHandler CreateHandler()
        {
            return new ReplaceUserRolesCommandHandler(UserManagerMock.Object, RoleManagerMock.Object);
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

        private static Mock<RoleManager<ApplicationRole>> CreateRoleManagerMock()
        {
            var store = new Mock<IRoleStore<ApplicationRole>>();
            return new Mock<RoleManager<ApplicationRole>>(
                store.Object,
                Array.Empty<IRoleValidator<ApplicationRole>>(),
                null!,
                null!,
                null!);
        }
    }
}
