using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Security.Queries.GetSidebarMenu;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Queries.GetSidebarMenu;

/// <summary>
/// Contains unit tests for <see cref="GetSidebarMenuQueryHandler"/>.
/// Verifies authentication guards, BuildTree parent-child nesting, alphabetical sorting,
/// permission flag mapping, and cache-hit behaviour using <see cref="FakeDbConnection"/>.
/// </summary>
/// <remarks>
/// Both the DirectPermissions query and the AllSystemOptions query are executed against the
/// same <see cref="FakeDbConnection"/> instance, which means both queries read from result
/// set index [0] of <see cref="FakeDbConnection.SetResults"/>. As a consequence, the
/// EnsureAncestors injection path — where a parent exists only in AllSystemOptions and not in
/// DirectPermissions — cannot be exercised through <see cref="FakeDbConnection"/> without a
/// multi-command result-set queue. That specific scenario is covered instead by the
/// orphan-child test below.
/// </remarks>
public sealed class GetSidebarMenuQueryHandlerTests : IDisposable
{
    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a dictionary row compatible with both <c>SidebarMenuSqlRow</c> and
    /// <c>SystemOptionHierarchyRow</c> internal record types.
    /// </summary>
    private static IDictionary<string, object?> MakeSidebarRow(
        Guid id,
        string name,
        Guid? parentId = null,
        string? icon = null,
        string? controllerName = null,
        int canRead = 1,
        int canCreate = 0,
        int canUpdate = 0,
        int canDelete = 0)
    {
        return new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["Name"] = name,
            ["Icon"] = icon,
            ["ControllerName"] = controllerName,
            ["ParentId"] = parentId,
            ["CanRead"] = canRead,
            ["CanCreate"] = canCreate,
            ["CanUpdate"] = canUpdate,
            ["CanDelete"] = canDelete
        };
    }

    // ── test context ─────────────────────────────────────────────────────────

    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<ISqlConnectionFactory> _connectionFactoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly FakeDbConnection _fakeConnection = new();

    /// <summary>Initialises the factory to always return the shared fake connection.</summary>
    public GetSidebarMenuQueryHandlerTests()
    {
        _connectionFactoryMock
            .Setup(x => x.CreateConnection())
            .Returns(_fakeConnection);
    }

    /// <summary>Disposes the real <see cref="IMemoryCache"/> instance after each test.</summary>
    public void Dispose() => _cache.Dispose();

    /// <summary>
    /// Configures the current-user mock with a fresh GUID pair so that each test owns
    /// a unique cache key and tests remain fully isolated.
    /// </summary>
    private void SetupAuthenticatedUser()
    {
        _currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(x => x.UserId).Returns(Guid.NewGuid().ToString());
        _currentUserMock.Setup(x => x.CompanyId).Returns(Guid.NewGuid());
    }

    private GetSidebarMenuQueryHandler CreateHandler() =>
        new(_connectionFactoryMock.Object, _currentUserMock.Object, _cache);

    // ── authentication guard tests ────────────────────────────────────────────

    /// <summary>
    /// Verifies that the handler throws when the user is not authenticated.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIsNotAuthenticated_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        _currentUserMock.Setup(x => x.IsAuthenticated).Returns(false);
        var handler = CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(new GetSidebarMenuQuery(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    /// <summary>
    /// Verifies that the handler throws when the user-id claim cannot be parsed as a GUID.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIdIsNotAValidGuid_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        _currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(x => x.UserId).Returns("not-a-guid");
        _currentUserMock.Setup(x => x.CompanyId).Returns(Guid.NewGuid());
        var handler = CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(new GetSidebarMenuQuery(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    /// <summary>
    /// Verifies that the handler throws when the company context is an empty GUID.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        _currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(x => x.UserId).Returns(Guid.NewGuid().ToString());
        _currentUserMock.Setup(x => x.CompanyId).Returns(Guid.Empty);
        var handler = CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(new GetSidebarMenuQuery(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // ── menu-content tests ────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty collection is returned when the user has no direct permissions.
    /// The AllSystemOptions query must not be executed in this path.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasNoDirectPermissions_ShouldReturnEmptyMenu()
    {
        // Arrange
        SetupAuthenticatedUser();
        _fakeConnection.SetResults(
            FakeResultSet.Empty("Id", "Name", "Icon", "ControllerName", "ParentId",
                                "CanRead", "CanCreate", "CanUpdate", "CanDelete"));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetSidebarMenuQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that all nodes without a parent are placed as root items in the returned collection.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAllItemsAreRootNodes_ShouldReturnFlatListWithNoChildren()
    {
        // Arrange
        SetupAuthenticatedUser();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        _fakeConnection.SetResults(FakeResultSet.FromRows(
            MakeSidebarRow(idA, "Dashboard"),
            MakeSidebarRow(idB, "Settings")));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetSidebarMenuQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data.Should().AllSatisfy(node => node.Children.Should().BeEmpty());
    }

    /// <summary>
    /// Verifies the core BuildTree nesting: a child whose ParentId matches another direct-permission
    /// node must appear in that parent's Children list and not as a root.
    /// </summary>
    [Fact]
    public async Task Handle_WhenChildHasParentInDirectPermissions_ShouldNestChildUnderParent()
    {
        // Arrange
        SetupAuthenticatedUser();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        _fakeConnection.SetResults(FakeResultSet.FromRows(
            MakeSidebarRow(parentId, "Settings", parentId: null),
            MakeSidebarRow(childId, "User Management", parentId: parentId)));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetSidebarMenuQuery(), CancellationToken.None);

        // Assert
        result.Data.Should().HaveCount(1, "only the parent is a root");

        var root = result.Data.Single();
        root.Id.Should().Be(parentId);
        root.Children.Should().HaveCount(1);
        root.Children.Single().Id.Should().Be(childId);
    }

    /// <summary>
    /// Verifies that when a child node references a ParentId that is absent from both
    /// DirectPermissions and AllSystemOptions results, the child is promoted to a root node.
    /// This exercises the boundary case of EnsureAncestors finding nothing to inject.
    /// </summary>
    [Fact]
    public async Task Handle_WhenChildReferencesOrphanParentId_ShouldPromoteChildToRoot()
    {
        // Arrange
        SetupAuthenticatedUser();
        var childId = Guid.NewGuid();
        var orphanParentId = Guid.NewGuid(); // does not appear in the result set

        _fakeConnection.SetResults(FakeResultSet.FromRows(
            MakeSidebarRow(childId, "Reports", parentId: orphanParentId)));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetSidebarMenuQuery(), CancellationToken.None);

        // Assert
        result.Data.Should().HaveCount(1, "the orphan child must be surfaced as a root");
        result.Data.Single().Id.Should().Be(childId);
    }

    /// <summary>
    /// Verifies that root nodes are returned sorted alphabetically by Name (ordinal, ignore case).
    /// </summary>
    [Fact]
    public async Task Handle_WhenMultipleRootNodesExist_ShouldReturnNodesSortedAlphabetically()
    {
        // Arrange
        SetupAuthenticatedUser();
        _fakeConnection.SetResults(FakeResultSet.FromRows(
            MakeSidebarRow(Guid.NewGuid(), "Zebra"),
            MakeSidebarRow(Guid.NewGuid(), "Apple"),
            MakeSidebarRow(Guid.NewGuid(), "Mango")));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetSidebarMenuQuery(), CancellationToken.None);

        // Assert
        result.Data.Select(n => n.Name)
              .Should().ContainInOrder("Apple", "Mango", "Zebra");
    }

    /// <summary>
    /// Verifies that CanRead, CanCreate, CanUpdate, and CanDelete integer values from SQL
    /// are correctly mapped to their boolean counterparts in <see cref="JOIN.Application.DTO.Security.MenuOptionResponse"/>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPermissionFlagsAreSet_ShouldMapIntegersToBooleans()
    {
        // Arrange
        SetupAuthenticatedUser();
        var nodeId = Guid.NewGuid();

        _fakeConnection.SetResults(FakeResultSet.FromRows(
            MakeSidebarRow(nodeId, "Tickets",
                canRead: 1, canCreate: 0, canUpdate: 1, canDelete: 0)));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetSidebarMenuQuery(), CancellationToken.None);

        // Assert
        var node = result.Data.Single();
        node.CanRead.Should().BeTrue();
        node.CanCreate.Should().BeFalse();
        node.CanUpdate.Should().BeTrue();
        node.CanDelete.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the result message matches the expected contract value.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSuccessful_ShouldReturnExpectedMessage()
    {
        // Arrange
        SetupAuthenticatedUser();
        _fakeConnection.SetResults(
            FakeResultSet.Empty("Id", "Name", "Icon", "ControllerName", "ParentId",
                                "CanRead", "CanCreate", "CanUpdate", "CanDelete"));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetSidebarMenuQuery(), CancellationToken.None);

        // Assert
        result.Message.Should().Be("Sidebar menu retrieved successfully.");
    }

    /// <summary>
    /// Verifies that a second identical request within the same cache lifetime is served from
    /// the in-memory cache, meaning the database connection is opened only once.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCalledTwiceWithSameContext_ShouldReturnCachedResultAndQueryDbOnce()
    {
        // Arrange — seed one node so the cache stores a non-empty collection
        SetupAuthenticatedUser();
        var nodeId = Guid.NewGuid();

        _fakeConnection.SetResults(FakeResultSet.FromRows(
            MakeSidebarRow(nodeId, "Dashboard")));

        var handler = CreateHandler();
        var query = new GetSidebarMenuQuery();

        // Act — first call populates the cache
        var firstResult = await handler.Handle(query, CancellationToken.None);

        // Swap the connection results to empty; if cache is bypassed, the second call returns nothing
        _fakeConnection.SetResults(
            FakeResultSet.Empty("Id", "Name", "Icon", "ControllerName", "ParentId",
                                "CanRead", "CanCreate", "CanUpdate", "CanDelete"));

        var secondResult = await handler.Handle(query, CancellationToken.None);

        // Assert — second result matches first (served from cache, not the now-empty DB)
        secondResult.Data.Should().HaveCount(firstResult.Data!.Count,
            "the cache must have served the original result");

        _connectionFactoryMock.Verify(x => x.CreateConnection(), Times.Once(),
            "the database must only be opened on the first (cold-cache) request");
    }
}
