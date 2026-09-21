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
/// Contains the unit tests for <see cref="AuthenticatedSessionIssuer"/>: effective company resolution,
/// role resolution, refresh-token persistence, and JWT issuance for an already-authenticated user.
/// These scenarios were moved out of <c>LoginCommandHandlerTests</c> when SPEC 32 (F1) extracted this
/// logic so it can be shared with the MFA challenge verification path.
/// </summary>
public sealed class AuthenticatedSessionIssuerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that a regular user can log in using a requested company header when they have access to it.
    /// </summary>
    [Fact]
    public async Task IssueAsync_WhenRegularUserRequestsSpecificCompany_ShouldUseRequestedCompanyAndResolvedRoles()
    {
        // Arrange
        var context = new AuthenticatedSessionIssuerTestContext();
        var requestedCompanyId = _fixture.Create<Guid>();
        var roleIdAdmin = _fixture.Create<Guid>();
        var roleIdManager = _fixture.Create<Guid>();
        var user = CreateUser();
        var expiration = DateTime.UtcNow.AddHours(1);
        var refreshExpiration = DateTime.UtcNow.AddDays(7);

        ArrangeAuthenticatedUser(context, user);

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
            .Setup(x => x.GenerateRefreshTokenString())
            .Returns("refresh-token");

        context.TokenGeneratorMock
            .Setup(x => x.GetRefreshTokenExpirationUtc())
            .Returns(refreshExpiration);

        context.TokenGeneratorMock
            .Setup(x => x.GenerateToken(
                user,
                requestedCompanyId,
                It.Is<IEnumerable<string>>(roles => roles.OrderBy(x => x).SequenceEqual(new[] { "Admin", "Manager" })),
                It.IsAny<Guid>(),
                It.IsAny<string>()))
            .Returns(("jwt-token", "refresh-token", expiration, refreshExpiration));

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var issuer = context.CreateIssuer();

        // Act
        var response = await issuer.IssueAsync(user, requestedCompanyId, CancellationToken.None);

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
    public async Task IssueAsync_WhenUserHasNoDefaultCompany_ShouldUseFirstAssignedCompany()
    {
        // Arrange
        var context = new AuthenticatedSessionIssuerTestContext();
        var assignedCompanyId = _fixture.Create<Guid>();
        var roleId = _fixture.Create<Guid>();
        var user = CreateUser();

        ArrangeAuthenticatedUser(context, user);

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
                It.Is<IEnumerable<string>>(roles => roles.Single() == "Agent"),
                It.IsAny<Guid>(),
                It.IsAny<string>()))
            .Returns(CreateTokenResult());

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var issuer = context.CreateIssuer();

        // Act
        var response = await issuer.IssueAsync(user, null, CancellationToken.None);

        // Assert
        response.CompanyId.Should().Be(assignedCompanyId);
        response.Roles.Should().ContainSingle().Which.Should().Be("Agent");
    }

    /// <summary>
    /// Verifies the fallback branch when the default company exists but is not usable for the role resolution context.
    /// </summary>
    [Fact]
    public async Task IssueAsync_WhenDefaultCompanyHasNoRoleAssignment_ShouldFallbackToFirstAssignedCompany()
    {
        // Arrange
        var context = new AuthenticatedSessionIssuerTestContext();
        var unsupportedCompanyId = _fixture.Create<Guid>();
        var defaultCompanyId = _fixture.Create<Guid>();
        var assignedCompanyId = _fixture.Create<Guid>();
        var roleId = _fixture.Create<Guid>();
        var user = CreateUser();

        ArrangeAuthenticatedUser(context, user);

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
                It.Is<IEnumerable<string>>(roles => roles.Single() == "Reader"),
                It.IsAny<Guid>(),
                It.IsAny<string>()))
            .Returns(CreateTokenResult());

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var issuer = context.CreateIssuer();

        // Act
        var response = await issuer.IssueAsync(user, unsupportedCompanyId, CancellationToken.None);

        // Assert
        response.CompanyId.Should().Be(assignedCompanyId);
        response.Roles.Should().ContainSingle().Which.Should().Be("Reader");
    }

    /// <summary>
    /// Regression test: a user can hold both IsSuperAdmin and IsSuperAdminCompany at once.
    /// The early SuperAdmin return must not drop the SuperAdminCompany claim — found live
    /// while re-testing specs/08 in join_frontb (a user with both flags got a JWT with only
    /// "SuperAdmin", 403'ing on every endpoint gated on "SuperAdminCompany" despite the flag
    /// being true in the database).
    /// </summary>
    [Fact]
    public async Task IssueAsync_WhenSuperAdminAlsoSuperAdminCompany_ShouldIncludeBothRoles()
    {
        // Arrange
        var context = new AuthenticatedSessionIssuerTestContext();
        var requestedCompanyId = _fixture.Create<Guid>();
        var user = CreateUser(isSuperAdmin: true, isSuperAdminCompany: true);

        ArrangeAuthenticatedUser(context, user);

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
                It.Is<IEnumerable<string>>(roles =>
                    roles.Contains("SuperAdmin") && roles.Contains("SuperAdminCompany") && roles.Count() == 2),
                It.IsAny<Guid>(),
                It.IsAny<string>()))
            .Returns(CreateTokenResult());

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var issuer = context.CreateIssuer();

        // Act
        var response = await issuer.IssueAsync(user, requestedCompanyId, CancellationToken.None);

        // Assert
        response.CompanyId.Should().Be(requestedCompanyId);
        response.Roles.Should().BeEquivalentTo(["SuperAdmin", "SuperAdminCompany"]);
    }

    /// <summary>
    /// Verifies the requested-company branch for a super administrator when the company exists.
    /// </summary>
    [Fact]
    public async Task IssueAsync_WhenSuperAdminRequestsSpecificCompany_ShouldUseRequestedCompany()
    {
        // Arrange
        var context = new AuthenticatedSessionIssuerTestContext();
        var requestedCompanyId = _fixture.Create<Guid>();
        var user = CreateUser(isSuperAdmin: true);

        ArrangeAuthenticatedUser(context, user);

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
                It.Is<IEnumerable<string>>(roles => roles.Single() == "SuperAdmin"),
                It.IsAny<Guid>(),
                It.IsAny<string>()))
            .Returns(CreateTokenResult());

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var issuer = context.CreateIssuer();

        // Act
        var response = await issuer.IssueAsync(user, requestedCompanyId, CancellationToken.None);

        // Assert
        response.CompanyId.Should().Be(requestedCompanyId);
        response.Roles.Should().ContainSingle().Which.Should().Be("SuperAdmin");
    }

    /// <summary>
    /// Verifies the fallback branch for a super administrator when no assignments exist and the first company is selected.
    /// </summary>
    [Fact]
    public async Task IssueAsync_WhenSuperAdminHasNoAssignments_ShouldFallbackToFirstCompany()
    {
        // Arrange
        var context = new AuthenticatedSessionIssuerTestContext();
        var fallbackCompanyId = _fixture.Create<Guid>();
        var user = CreateUser(isSuperAdmin: true);

        ArrangeAuthenticatedUser(context, user);

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
                It.Is<IEnumerable<string>>(roles => roles.Single() == "SuperAdmin"),
                It.IsAny<Guid>(),
                It.IsAny<string>()))
            .Returns(CreateTokenResult());

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var issuer = context.CreateIssuer();

        // Act
        var response = await issuer.IssueAsync(user, null, CancellationToken.None);

        // Assert
        response.CompanyId.Should().Be(fallbackCompanyId);
        response.Roles.Should().ContainSingle().Which.Should().Be("SuperAdmin");
    }

    /// <summary>
    /// Verifies the null-company branch for a regular user with no memberships or role assignments.
    /// </summary>
    [Fact]
    public async Task IssueAsync_WhenRegularUserHasNoCompaniesOrRoles_ShouldReturnNullCompanyAndBasicRole()
    {
        // Arrange
        var context = new AuthenticatedSessionIssuerTestContext();
        var user = CreateUser();

        ArrangeAuthenticatedUser(context, user);

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
                It.Is<IEnumerable<string>>(roles => roles.Single() == "Basic"),
                It.IsAny<Guid>(),
                It.IsAny<string>()))
            .Returns(CreateTokenResult());

        context.RefreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<UserRefreshToken>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var issuer = context.CreateIssuer();

        // Act
        var response = await issuer.IssueAsync(user, null, CancellationToken.None);

        // Assert
        response.CompanyId.Should().BeNull();
        response.Roles.Should().ContainSingle().Which.Should().Be("Basic");
    }

    /// <summary>
    /// Verifies that a failure to persist the refresh token surfaces as an unauthorized session error
    /// instead of leaking a JWT whose refresh row never made it to the database.
    /// </summary>
    [Fact]
    public async Task IssueAsync_WhenRefreshTokenPersistenceFails_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new AuthenticatedSessionIssuerTestContext();
        var user = CreateUser();

        ArrangeAuthenticatedUser(context, user);

        context.UserCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserCompany>());

        context.UserRoleCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserRoleCompany>());

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var issuer = context.CreateIssuer();

        // Act
        Func<Task> act = () => issuer.IssueAsync(user, null, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("The session could not be established.");
    }

    /// <summary>
    /// Arranges the reusable "SuperAdmin role membership" lookup used by the resolution branches.
    /// </summary>
    private static void ArrangeAuthenticatedUser(
        AuthenticatedSessionIssuerTestContext context,
        ApplicationUser user,
        bool isInSuperAdminRole = false)
    {
        context.UserManagerMock
            .Setup(x => x.IsInRoleAsync(user, "SuperAdmin"))
            .ReturnsAsync(isInSuperAdminRole);
    }

    /// <summary>
    /// Creates a valid user for the authentication scenarios.
    /// </summary>
    private static ApplicationUser CreateUser(bool isSuperAdmin = false, bool isSuperAdminCompany = false)
    {
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "join.user",
            Email = "user@join.com",
            IsActive = true,
            IsSuperAdmin = isSuperAdmin,
            IsSuperAdminCompany = isSuperAdminCompany,
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
    /// Holds the reusable mocks and helper factory for the session issuer.
    /// </summary>
    private sealed class AuthenticatedSessionIssuerTestContext
    {
        public AuthenticatedSessionIssuerTestContext()
        {
            SetupRepository(UnitOfWorkMock, UserCompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserRoleCompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, RefreshTokenRepositoryMock);
            SetupRepository(UnitOfWorkMock, RoleRepositoryMock);
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);

            CurrentUserServiceMock.SetupGet(x => x.IpAddress).Returns("127.0.0.1");
            CurrentUserServiceMock.SetupGet(x => x.UserAgent).Returns("unit-test");
        }

        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } = CreateUserManagerMock();
        public Mock<IJwtTokenGenerator> TokenGeneratorMock { get; } = new();
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<UserCompany>> UserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserRoleCompany>> UserRoleCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserRefreshToken>> RefreshTokenRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationRole>> RoleRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ISecurityEventLogger> SecurityEventLoggerMock { get; } = new();

        public AuthenticatedSessionIssuer CreateIssuer()
        {
            return new AuthenticatedSessionIssuer(
                UserManagerMock.Object,
                TokenGeneratorMock.Object,
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                SecurityEventLoggerMock.Object);
        }
    }
}
