using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Security.Auth.Refresh;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Auth.Refresh;

/// <summary>
/// Contains the unit tests for the refresh token rotation flow.
/// These tests verify token validation, account validation, token rotation persistence,
/// and the company resolution branches during session renewal.
/// </summary>
public sealed class RefreshTokenCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the unauthorized branch when the provided refresh token does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRefreshTokenDoesNotExist_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new RefreshTokenCommandHandlerTestContext();
        var request = new RefreshTokenCommand { RefreshToken = "missing-token" };

        context.RefreshTokenRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserRefreshToken>());

        var handler = context.CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("The refresh token is invalid, expired, or revoked.");
    }

    /// <summary>
    /// Verifies the unauthorized branch when the provided refresh token has been revoked.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRefreshTokenIsRevoked_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new RefreshTokenCommandHandlerTestContext();
        var request = new RefreshTokenCommand { RefreshToken = "revoked-token" };

        context.RefreshTokenRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new UserRefreshToken
                {
                    UserId = _fixture.Create<Guid>(),
                    Token = "revoked-token",
                    ExpiryDate = DateTime.UtcNow.AddDays(2),
                    IsRevoked = true
                }
            }.AsEnumerable());

        var handler = context.CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("The refresh token is invalid, expired, or revoked.");
    }

    /// <summary>
    /// Verifies the unauthorized branch when the provided refresh token is expired.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRefreshTokenIsExpired_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new RefreshTokenCommandHandlerTestContext();
        var userId = _fixture.Create<Guid>();
        var request = new RefreshTokenCommand { RefreshToken = "expired-token" };

        context.RefreshTokenRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new UserRefreshToken
                {
                    UserId = userId,
                    Token = "expired-token",
                    ExpiryDate = DateTime.UtcNow.AddMinutes(-1),
                    IsRevoked = false
                }
            }.AsEnumerable());

        var handler = context.CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("The refresh token is invalid, expired, or revoked.");
    }

    /// <summary>
    /// Verifies the unauthorized branch when the refresh token user can no longer be resolved.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRefreshTokenUserDoesNotExist_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new RefreshTokenCommandHandlerTestContext();
        var storedToken = CreateRefreshToken(Guid.NewGuid(), "valid-token");
        var request = new RefreshTokenCommand { RefreshToken = "valid-token" };

        context.RefreshTokenRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { storedToken }.AsEnumerable());

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(storedToken.UserId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        var handler = context.CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("The refresh token user is no longer available.");
    }

    /// <summary>
    /// Verifies the unauthorized branch when the user account is inactive during refresh.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRefreshTokenUserIsInactive_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new RefreshTokenCommandHandlerTestContext();
        var user = CreateUser(isActive: false);
        var storedToken = CreateRefreshToken(user.Id, "valid-token");
        var request = new RefreshTokenCommand { RefreshToken = "valid-token" };

        context.RefreshTokenRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { storedToken }.AsEnumerable());

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
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
    /// Verifies the unauthorized branch when the user was soft-deleted during refresh.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRefreshTokenUserIsSoftDeleted_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new RefreshTokenCommandHandlerTestContext();
        var user = CreateUser();
        user.GcRecord = 20260420;
        var storedToken = CreateRefreshToken(user.Id, "valid-token");
        var request = new RefreshTokenCommand { RefreshToken = "valid-token" };

        context.RefreshTokenRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { storedToken }.AsEnumerable());

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
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
    /// Verifies the happy path when the token is valid and the default company is still usable.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTokenIsValid_ShouldRotateTokensAndReturnRenewedSession()
    {
        // Arrange
        var context = new RefreshTokenCommandHandlerTestContext();
        var companyId = _fixture.Create<Guid>();
        var roleId = _fixture.Create<Guid>();
        var user = CreateUser();
        var storedToken = CreateRefreshToken(user.Id, "current-refresh");
        var request = new RefreshTokenCommand { RefreshToken = "current-refresh" };
        var expiration = DateTime.UtcNow.AddHours(2);
        var refreshExpiration = DateTime.UtcNow.AddDays(10);

        ArrangeRefreshUser(context, user);

        context.RefreshTokenRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { storedToken }.AsEnumerable());

        context.RefreshTokenRepositoryMock
            .Setup(x => x.UpdateAsync(storedToken))
            .ReturnsAsync(true);

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UserCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new UserCompany
                {
                    UserId = user.Id,
                    CompanyId = companyId,
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
                    CompanyId = companyId,
                    RoleId = roleId
                }
            }.AsEnumerable());

        context.RoleRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new ApplicationRole { Id = roleId, Name = "Admin" }
            }.AsEnumerable());

        context.TokenGeneratorMock
            .Setup(x => x.GenerateToken(
                user,
                companyId,
                It.Is<IEnumerable<string>>(roles => roles.Single() == "Admin")))
            .Returns(("new-access", "rotated-refresh", expiration, refreshExpiration));

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.UserId.Should().Be(user.Id);
        response.CompanyId.Should().Be(companyId);
        response.Roles.Should().ContainSingle().Which.Should().Be("Admin");
        response.Token.Should().Be("new-access");
        response.RefreshToken.Should().Be("rotated-refresh");
        response.Expiration.Should().Be(expiration);

        storedToken.IsRevoked.Should().BeTrue();
        storedToken.LastModifiedBy.Should().Be(user.Email);

        context.RefreshTokenRepositoryMock.Verify(x => x.UpdateAsync(storedToken), Times.Once);
        context.RefreshTokenRepositoryMock.Verify(x => x.InsertAsync(It.Is<UserRefreshToken>(token =>
            token.UserId == user.Id
            && token.Token == "rotated-refresh"
            && token.IsRevoked == false
            && token.CreatedBy == user.Email)), Times.Once);
    }

    /// <summary>
    /// Verifies the branch where no default company exists and the first assigned company is selected.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasNoDefaultCompany_ShouldUseFirstAssignedCompany()
    {
        // Arrange
        var context = new RefreshTokenCommandHandlerTestContext();
        var assignedCompanyId = _fixture.Create<Guid>();
        var roleId = _fixture.Create<Guid>();
        var user = CreateUser();
        var storedToken = CreateRefreshToken(user.Id, "current-refresh");
        var request = new RefreshTokenCommand { RefreshToken = "current-refresh" };

        ArrangeRefreshUser(context, user);

        context.RefreshTokenRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { storedToken }.AsEnumerable());

        context.RefreshTokenRepositoryMock
            .Setup(x => x.UpdateAsync(storedToken))
            .ReturnsAsync(true);

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

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
                new ApplicationRole { Id = roleId, Name = "Reader" }
            }.AsEnumerable());

        context.TokenGeneratorMock
            .Setup(x => x.GenerateToken(
                user,
                assignedCompanyId,
                It.Is<IEnumerable<string>>(roles => roles.Single() == "Reader")))
            .Returns(CreateTokenResult());

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
    /// Verifies the fallback branch for a super administrator when no assignments exist and the first company is selected.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSuperAdminHasNoAssignments_ShouldFallbackToFirstCompany()
    {
        // Arrange
        var context = new RefreshTokenCommandHandlerTestContext();
        var fallbackCompanyId = _fixture.Create<Guid>();
        var user = CreateUser(isSuperAdmin: true);
        var storedToken = CreateRefreshToken(user.Id, "current-refresh");
        var request = new RefreshTokenCommand { RefreshToken = "current-refresh" };

        ArrangeRefreshUser(context, user);

        context.RefreshTokenRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { storedToken }.AsEnumerable());

        context.RefreshTokenRepositoryMock
            .Setup(x => x.UpdateAsync(storedToken))
            .ReturnsAsync(true);

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UserCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserCompany>());

        context.UserRoleCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserRoleCompany>());

        context.CompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { CreateCompany(fallbackCompanyId) }.AsEnumerable());

        context.TokenGeneratorMock
            .Setup(x => x.GenerateToken(
                user,
                fallbackCompanyId,
                It.Is<IEnumerable<string>>(roles => roles.Single() == "SuperAdmin")))
            .Returns(CreateTokenResult());

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
    /// Creates a refresh token entity suitable for token rotation scenarios.
    /// </summary>
    private static UserRefreshToken CreateRefreshToken(Guid userId, string token)
    {
        return new UserRefreshToken
        {
            UserId = userId,
            Token = token,
            ExpiryDate = DateTime.UtcNow.AddDays(7),
            IsRevoked = false
        };
    }

    /// <summary>
    /// Arranges the reusable user resolution setup for the refresh flow.
    /// </summary>
    private static void ArrangeRefreshUser(
        RefreshTokenCommandHandlerTestContext context,
        ApplicationUser user,
        bool isInSuperAdminRole = false)
    {
        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        context.UserManagerMock
            .Setup(x => x.IsInRoleAsync(user, "SuperAdmin"))
            .ReturnsAsync(isInSuperAdminRole);
    }

    /// <summary>
    /// Creates a valid user for the refresh token scenarios.
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
    /// Creates a company with a deterministic identifier for the super admin fallback scenario.
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
            "new-access",
            "rotated-refresh",
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
    /// Holds the reusable mocks and helper factory for the refresh handler.
    /// </summary>
    private sealed class RefreshTokenCommandHandlerTestContext
    {
        public RefreshTokenCommandHandlerTestContext()
        {
            SetupRepository(UnitOfWorkMock, RefreshTokenRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserCompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserRoleCompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, RoleRepositoryMock);
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
        }

        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } = CreateUserManagerMock();
        public Mock<IJwtTokenGenerator> TokenGeneratorMock { get; } = new();
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<UserRefreshToken>> RefreshTokenRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserCompany>> UserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserRoleCompany>> UserRoleCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationRole>> RoleRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();

        public RefreshTokenCommandHandler CreateHandler()
        {
            return new RefreshTokenCommandHandler(
                UserManagerMock.Object,
                TokenGeneratorMock.Object,
                UnitOfWorkMock.Object);
        }
    }
}
