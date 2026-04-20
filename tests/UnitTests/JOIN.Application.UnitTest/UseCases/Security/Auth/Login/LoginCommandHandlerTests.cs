using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Auth.Login;

/// <summary>
/// Contains the unit tests for the login authentication flow.
/// These tests verify credential validation, token generation, refresh token persistence,
/// and the effective company resolution branches.
/// </summary>
public sealed class LoginCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the unauthorized branch when the email does not match any user.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var request = CreateValidCommand();

        context.UserManagerMock
            .Setup(x => x.FindByEmailAsync(request.Email.Trim()))
            .ReturnsAsync((ApplicationUser?)null);

        var handler = context.CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid email or password.");
    }

    /// <summary>
    /// Verifies the unauthorized branch when the user account is inactive.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIsInactive_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var request = CreateValidCommand();
        var user = CreateUser(isActive: false);

        context.UserManagerMock
            .Setup(x => x.FindByEmailAsync(request.Email.Trim()))
            .ReturnsAsync(user);

        var handler = context.CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("The user account is inactive.");
    }

    /// <summary>
    /// Verifies the unauthorized branch when the user was soft-deleted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIsSoftDeleted_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var request = CreateValidCommand();
        var user = CreateUser();
        user.GcRecord = 20260420;

        context.UserManagerMock
            .Setup(x => x.FindByEmailAsync(request.Email.Trim()))
            .ReturnsAsync(user);

        var handler = context.CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("The user account is inactive.");
    }

    /// <summary>
    /// Verifies the unauthorized branch when the supplied password is invalid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPasswordIsInvalid_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var request = CreateValidCommand();
        var user = CreateUser();

        context.UserManagerMock
            .Setup(x => x.FindByEmailAsync(request.Email.Trim()))
            .ReturnsAsync(user);

        context.UserManagerMock
            .Setup(x => x.CheckPasswordAsync(user, request.Password))
            .ReturnsAsync(false);

        var handler = context.CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid email or password.");
    }

    /// <summary>
    /// Verifies that a regular user can log in using a requested company header when they have access to it.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRegularUserRequestsSpecificCompany_ShouldUseRequestedCompanyAndResolvedRoles()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var requestedCompanyId = _fixture.Create<Guid>();
        var roleIdAdmin = _fixture.Create<Guid>();
        var roleIdManager = _fixture.Create<Guid>();
        var request = CreateValidCommand(requestedCompanyId);
        var user = CreateUser();
        var expiration = DateTime.UtcNow.AddHours(1);
        var refreshExpiration = DateTime.UtcNow.AddDays(7);

        ArrangeAuthenticatedUser(context, user, request);

        context.UserCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new UserCompany
                {
                    UserId = user.Id,
                    CompanyId = requestedCompanyId,
                    IsDefault = true,
                    Created = DateTime.UtcNow.AddDays(-1)
                }
            }.AsEnumerable());

        context.UserRoleCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new UserRoleCompany { UserId = user.Id, CompanyId = requestedCompanyId, RoleId = roleIdManager },
                new UserRoleCompany { UserId = user.Id, CompanyId = requestedCompanyId, RoleId = roleIdAdmin }
            }.AsEnumerable());

        context.RoleRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new ApplicationRole { Id = roleIdManager, Name = "Manager" },
                new ApplicationRole { Id = roleIdAdmin, Name = "Admin" }
            }.AsEnumerable());

        context.TokenGeneratorMock
            .Setup(x => x.GenerateToken(
                user,
                requestedCompanyId,
                It.Is<IEnumerable<string>>(roles => roles.OrderBy(x => x).SequenceEqual(new[] { "Admin", "Manager" }))))
            .Returns(("jwt-token", "refresh-token", expiration, refreshExpiration));

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.UserId.Should().Be(user.Id);
        response.Email.Should().Be("user@join.com");
        response.CompanyId.Should().Be(requestedCompanyId);
        response.Roles.Should().BeEquivalentTo(new[] { "Admin", "Manager" });
        response.Token.Should().Be("jwt-token");
        response.RefreshToken.Should().Be("refresh-token");
        response.Expiration.Should().Be(expiration);

        context.RefreshTokenRepositoryMock.Verify(x => x.InsertAsync(It.Is<UserRefreshToken>(token =>
            token.UserId == user.Id
            && token.Token == "refresh-token"
            && token.IsRevoked == false
            && token.CreatedBy == user.Email)), Times.Once);
    }

    /// <summary>
    /// Verifies the explicit branch where a user has no default company and the first assigned company is selected.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasNoDefaultCompany_ShouldUseFirstAssignedCompany()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var assignedCompanyId = _fixture.Create<Guid>();
        var roleId = _fixture.Create<Guid>();
        var request = CreateValidCommand();
        var user = CreateUser();

        ArrangeAuthenticatedUser(context, user, request);

        context.UserCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserCompany>());

        context.UserRoleCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new UserRoleCompany
                {
                    UserId = user.Id,
                    CompanyId = assignedCompanyId,
                    RoleId = roleId
                }
            }.AsEnumerable());

        context.RoleRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new ApplicationRole { Id = roleId, Name = "Agent" }
            }.AsEnumerable());

        context.TokenGeneratorMock
            .Setup(x => x.GenerateToken(
                user,
                assignedCompanyId,
                It.Is<IEnumerable<string>>(roles => roles.Single() == "Agent")))
            .Returns(CreateTokenResult());

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.CompanyId.Should().Be(assignedCompanyId);
        response.Roles.Should().ContainSingle().Which.Should().Be("Agent");
    }

    /// <summary>
    /// Verifies the fallback branch when the default company exists but is not usable for the role resolution context.
    /// </summary>
    [Fact]
    public async Task Handle_WhenDefaultCompanyHasNoRoleAssignment_ShouldFallbackToFirstAssignedCompany()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var unsupportedCompanyId = _fixture.Create<Guid>();
        var defaultCompanyId = _fixture.Create<Guid>();
        var assignedCompanyId = _fixture.Create<Guid>();
        var roleId = _fixture.Create<Guid>();
        var request = CreateValidCommand(unsupportedCompanyId);
        var user = CreateUser();

        ArrangeAuthenticatedUser(context, user, request);

        context.UserCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new UserCompany
                {
                    UserId = user.Id,
                    CompanyId = defaultCompanyId,
                    IsDefault = true,
                    Created = DateTime.UtcNow
                }
            }.AsEnumerable());

        context.UserRoleCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new UserRoleCompany
                {
                    UserId = user.Id,
                    CompanyId = assignedCompanyId,
                    RoleId = roleId
                }
            }.AsEnumerable());

        context.RoleRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new ApplicationRole { Id = roleId, Name = "Reader" }
            }.AsEnumerable());

        context.TokenGeneratorMock
            .Setup(x => x.GenerateToken(
                user,
                assignedCompanyId,
                It.Is<IEnumerable<string>>(roles => roles.Single() == "Reader")))
            .Returns(CreateTokenResult());

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.CompanyId.Should().Be(assignedCompanyId);
        response.Roles.Should().ContainSingle().Which.Should().Be("Reader");
    }

    /// <summary>
    /// Verifies the requested-company branch for a super administrator when the company exists.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSuperAdminRequestsSpecificCompany_ShouldUseRequestedCompany()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var requestedCompanyId = _fixture.Create<Guid>();
        var request = CreateValidCommand(requestedCompanyId);
        var user = CreateUser(isSuperAdmin: true);

        ArrangeAuthenticatedUser(context, user, request);

        context.UserCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserCompany>());

        context.UserRoleCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserRoleCompany>());

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(requestedCompanyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.TokenGeneratorMock
            .Setup(x => x.GenerateToken(
                user,
                requestedCompanyId,
                It.Is<IEnumerable<string>>(roles => roles.Single() == "SuperAdmin")))
            .Returns(CreateTokenResult());

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.CompanyId.Should().Be(requestedCompanyId);
        response.Roles.Should().ContainSingle().Which.Should().Be("SuperAdmin");
    }

    /// <summary>
    /// Verifies the fallback branch for a super administrator when no assignments exist and the first company is selected.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSuperAdminHasNoAssignments_ShouldFallbackToFirstCompany()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var fallbackCompanyId = _fixture.Create<Guid>();
        var request = CreateValidCommand();
        var user = CreateUser(isSuperAdmin: true);

        ArrangeAuthenticatedUser(context, user, request);

        context.UserCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserCompany>());

        context.UserRoleCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserRoleCompany>());

        context.CompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                CreateCompany(fallbackCompanyId)
            }.AsEnumerable());

        context.TokenGeneratorMock
            .Setup(x => x.GenerateToken(
                user,
                fallbackCompanyId,
                It.Is<IEnumerable<string>>(roles => roles.Single() == "SuperAdmin")))
            .Returns(CreateTokenResult());

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.CompanyId.Should().Be(fallbackCompanyId);
        response.Roles.Should().ContainSingle().Which.Should().Be("SuperAdmin");
    }

    /// <summary>
    /// Verifies the null-company branch for a regular user with no memberships or role assignments.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRegularUserHasNoCompaniesOrRoles_ShouldReturnNullCompanyAndBasicRole()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var request = CreateValidCommand();
        var user = CreateUser();

        ArrangeAuthenticatedUser(context, user, request);

        context.UserCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserCompany>());

        context.UserRoleCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserRoleCompany>());

        context.TokenGeneratorMock
            .Setup(x => x.GenerateToken(
                user,
                null,
                It.Is<IEnumerable<string>>(roles => roles.Single() == "Basic")))
            .Returns(CreateTokenResult());

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.CompanyId.Should().BeNull();
        response.Roles.Should().ContainSingle().Which.Should().Be("Basic");
    }

    /// <summary>
    /// Arranges the reusable authentication success setup for the mocked user manager.
    /// </summary>
    private static void ArrangeAuthenticatedUser(
        LoginCommandHandlerTestContext context,
        ApplicationUser user,
        LoginCommand request,
        bool isInSuperAdminRole = false)
    {
        context.UserManagerMock
            .Setup(x => x.FindByEmailAsync(request.Email.Trim()))
            .ReturnsAsync(user);

        context.UserManagerMock
            .Setup(x => x.CheckPasswordAsync(user, request.Password))
            .ReturnsAsync(true);

        context.UserManagerMock
            .Setup(x => x.IsInRoleAsync(user, "SuperAdmin"))
            .ReturnsAsync(isInSuperAdminRole);
    }

    /// <summary>
    /// Creates a valid login command for the authentication flow.
    /// </summary>
    private static LoginCommand CreateValidCommand(Guid? targetCompanyId = null)
    {
        return new LoginCommand
        {
            Email = "  user@join.com  ",
            Password = "Pass123!",
            TargetCompanyId = targetCompanyId
        };
    }

    /// <summary>
    /// Creates a valid user for the authentication scenarios.
    /// </summary>
    private static ApplicationUser CreateUser(bool isActive = true, bool isSuperAdmin = false)
    {
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "join.user",
            Email = "user@join.com",
            IsActive = isActive,
            IsSuperAdmin = isSuperAdmin,
            GcRecord = 0
        };
    }

    /// <summary>
    /// Creates a company with a deterministic identifier for branch validation.
    /// </summary>
    private static Company CreateCompany(Guid companyId)
    {
        var company = new Company
        {
            Name = "JOIN CRM",
            TaxId = "RUC"
        };

        typeof(Company).GetProperty(nameof(Company.Id))!.SetValue(company, companyId);
        return company;
    }

    /// <summary>
    /// Creates a deterministic token tuple used by the mocked token generator.
    /// </summary>
    private static (string Token, string RefreshToken, DateTime Expiration, DateTime RefreshTokenExpiration) CreateTokenResult()
    {
        return (
            "jwt-token",
            "refresh-token",
            DateTime.UtcNow.AddHours(1),
            DateTime.UtcNow.AddDays(7));
    }

    /// <summary>
    /// Creates a mockable ASP.NET Core user manager instance.
    /// </summary>
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

    /// <summary>
    /// Registers a repository in the mocked unit of work using the generic resolution pattern.
    /// </summary>
    private static void SetupRepository<TEntity>(
        Mock<IUnitOfWork> unitOfWorkMock,
        Mock<IGenericRepository<TEntity>> repositoryMock)
        where TEntity : class
    {
        unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the login handler.
    /// </summary>
    private sealed class LoginCommandHandlerTestContext
    {
        public LoginCommandHandlerTestContext()
        {
            SetupRepository(UnitOfWorkMock, UserCompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserRoleCompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, RefreshTokenRepositoryMock);
            SetupRepository(UnitOfWorkMock, RoleRepositoryMock);
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
        }

        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } = CreateUserManagerMock();
        public Mock<IJwtTokenGenerator> TokenGeneratorMock { get; } = new();
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<UserCompany>> UserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserRoleCompany>> UserRoleCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserRefreshToken>> RefreshTokenRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationRole>> RoleRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();

        public LoginCommandHandler CreateHandler()
        {
            return new LoginCommandHandler(
                UserManagerMock.Object,
                TokenGeneratorMock.Object,
                UnitOfWorkMock.Object);
        }
    }
}
