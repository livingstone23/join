using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace JOIN.Application.UseCases.Security.Queries.GetSidebarMenu;

/// <summary>
/// Handles the retrieval of the sidebar menu for the current authenticated user.
/// Any option assigned through the user's company-scoped roles is returned, even when the permission flags are inconsistently configured.
/// </summary>
/// <param name="connectionFactory">Factory used to create engine-agnostic read connections.</param>
/// <param name="currentUserService">Current user context used to resolve the active tenant.</param>
/// <param name="memoryCache">In-memory cache used to avoid rebuilding the same menu repeatedly.</param>
public sealed class GetSidebarMenuQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    IMemoryCache memoryCache)
    : IRequestHandler<GetSidebarMenuQuery, Response<IReadOnlyCollection<MenuOptionResponse>>>
{
    private const string DirectPermissionsSql = """
        SELECT
            so.Id,
            so.Name,
            so.Icon,
            so.ControllerName,
            so.ParentId,
            MAX(CAST(rso.CanRead AS INT)) AS CanRead,
            MAX(CAST(rso.CanCreate AS INT)) AS CanCreate,
            MAX(CAST(rso.CanUpdate AS INT)) AS CanUpdate,
            MAX(CAST(rso.CanDelete AS INT)) AS CanDelete
        FROM Security.SystemOptions so
        INNER JOIN Security.RoleSystemOptions rso
            ON rso.SystemOptionId = so.Id
           AND rso.CompanyId = @CompanyId
           AND rso.GcRecord = 0
        INNER JOIN Security.UserRoleCompanies urc
            ON urc.RoleId = rso.RoleId
           AND urc.UserId = @UserId
           AND urc.CompanyId = @CompanyId
           AND urc.GcRecord = 0
        WHERE so.GcRecord = 0
        GROUP BY
            so.Id,
            so.ModuleId,
            so.Name,
            so.Route,
            so.Icon,
            so.ParentId,
            so.ControllerName,
            so.CanRead,
            so.CanCreate,
            so.CanUpdate,
            so.CanDelete
        ORDER BY so.Name;
        """;

    private const string AllSystemOptionsSql = """
        SELECT
            so.Id,
            so.Name,
            so.Icon,
            so.ControllerName,
            so.ParentId
        FROM Security.SystemOptions so
        WHERE so.GcRecord = 0
        ORDER BY
            CASE WHEN so.ParentId IS NULL THEN 0 ELSE 1 END,
            so.Name;
        """;

    /// <summary>
    /// Resolves the permitted sidebar menu and returns it as a hierarchical tree.
    /// </summary>
    public async Task<Response<IReadOnlyCollection<MenuOptionResponse>>> Handle(
        GetSidebarMenuQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated
            || !Guid.TryParse(currentUserService.UserId, out var userId)
            || userId == Guid.Empty
            || currentUserService.CompanyId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("The authenticated user is not bound to a valid company context.");
        }

        var companyId = currentUserService.CompanyId;
        var cacheKey = $"sidebar:{companyId}:{userId}";

        var menu = await memoryCache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20);

            using var connection = connectionFactory.CreateConnection();
            var parameters = new DynamicParameters();
            parameters.Add("UserId", userId);
            parameters.Add("CompanyId", companyId);

            var rows = (await connection.QueryAsync<SidebarMenuSqlRow>(
                new CommandDefinition(DirectPermissionsSql, parameters, cancellationToken: cancellationToken)))
                .AsList();

            if (rows.Count == 0)
            {
                return Array.Empty<MenuOptionResponse>();
            }

            var allOptions = (await connection.QueryAsync<SystemOptionHierarchyRow>(
                new CommandDefinition(AllSystemOptionsSql, cancellationToken: cancellationToken)))
                .AsList();

            return BuildTree(rows, allOptions);
        }) ?? [];

        return new Response<IReadOnlyCollection<MenuOptionResponse>>
        {
            IsSuccess = true,
            Message = "Sidebar menu retrieved successfully.",
            Data = menu
        };
    }

    private static IReadOnlyCollection<MenuOptionResponse> BuildTree(
        IReadOnlyCollection<SidebarMenuSqlRow> rows,
        IReadOnlyCollection<SystemOptionHierarchyRow> allOptions)
    {
        var lookup = new Dictionary<Guid, MenuOptionResponse>(rows.Count);
        var optionIndex = allOptions.ToDictionary(option => option.Id);

        foreach (var row in rows)
        {
            lookup[row.Id] = new MenuOptionResponse
            {
                Id = row.Id,
                Name = row.Name,
                Icon = row.Icon,
                ControllerName = row.ControllerName,
                ParentId = row.ParentId,
                CanRead = row.CanRead == 1,
                CanCreate = row.CanCreate == 1,
                CanUpdate = row.CanUpdate == 1,
                CanDelete = row.CanDelete == 1,
                Children = []
            };
        }

        foreach (var row in rows)
        {
            EnsureAncestors(row.ParentId, lookup, optionIndex);
        }

        var roots = new List<MenuOptionResponse>();

        foreach (var item in lookup.Values)
        {
            if (item.ParentId.HasValue && lookup.TryGetValue(item.ParentId.Value, out var parent))
            {
                parent.Children.Add(item);
                continue;
            }

            roots.Add(item);
        }

        SortTree(roots);
        return roots;
    }

    private static void EnsureAncestors(
        Guid? parentId,
        IDictionary<Guid, MenuOptionResponse> lookup,
        IReadOnlyDictionary<Guid, SystemOptionHierarchyRow> optionIndex)
    {
        while (parentId.HasValue
               && !lookup.ContainsKey(parentId.Value)
               && optionIndex.TryGetValue(parentId.Value, out var parentRow))
        {
            lookup[parentRow.Id] = new MenuOptionResponse
            {
                Id = parentRow.Id,
                Name = parentRow.Name,
                Icon = parentRow.Icon,
                ControllerName = parentRow.ControllerName,
                ParentId = parentRow.ParentId,
                CanRead = false,
                CanCreate = false,
                CanUpdate = false,
                CanDelete = false,
                Children = []
            };

            parentId = parentRow.ParentId;
        }
    }

    private static void SortTree(List<MenuOptionResponse> nodes)
    {
        nodes.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));

        foreach (var node in nodes)
        {
            SortTree(node.Children);
        }
    }

    private sealed class SidebarMenuSqlRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string? ControllerName { get; set; }
        public Guid? ParentId { get; set; }
        public int CanRead { get; set; }
        public int CanCreate { get; set; }
        public int CanUpdate { get; set; }
        public int CanDelete { get; set; }
    }

    private sealed class SystemOptionHierarchyRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string? ControllerName { get; set; }
        public Guid? ParentId { get; set; }
    }
}
