using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Security.Users.Queries.GetUsersWithRoles;

/// <summary>
/// Handles the retrieval of all active users and their assigned roles.
/// </summary>
/// <param name="connectionFactory">Factory used to create engine-agnostic read connections.</param>
public sealed class GetUsersWithRolesQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetUsersWithRolesQuery, Response<IEnumerable<UserWithRolesDto>>>
{
    private const string Sql = """
        SELECT
            u.Id,
            u.UserName,
            u.Email,
            u.IsActive,
            r.Name AS RoleName
        FROM Security.Users u
        LEFT JOIN Security.UserRoles ur
            ON ur.UserId = u.Id
        LEFT JOIN Security.Roles r
            ON r.Id = ur.RoleId
           AND r.GcRecord = 0
        WHERE u.GcRecord = 0
        ORDER BY u.UserName, r.Name;
        """;

    /// <summary>
    /// Retrieves the active users and aggregates their assigned roles.
    /// </summary>
    /// <param name="request">The query payload.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A standardized response with the user/role projection.</returns>
    public async Task<Response<IEnumerable<UserWithRolesDto>>> Handle(
        GetUsersWithRolesQuery request,
        CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        var rows = (await connection.QueryAsync<UserWithRolesSqlRow>(
            new CommandDefinition(Sql, cancellationToken: cancellationToken)))
            .AsList();

        var users = rows
            .GroupBy(row => new { row.Id, row.UserName, row.Email, row.IsActive })
            .Select(group => new UserWithRolesDto
            {
                Id = group.Key.Id,
                UserName = group.Key.UserName ?? string.Empty,
                Email = group.Key.Email ?? string.Empty,
                IsActive = group.Key.IsActive,
                Roles = group
                    .Select(item => item.RoleName)
                    .Where(roleName => !string.IsNullOrWhiteSpace(roleName))
                    .Select(roleName => roleName!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(roleName => roleName)
                    .ToArray()
            })
            .OrderBy(user => user.UserName)
            .ToArray();

        return new Response<IEnumerable<UserWithRolesDto>>
        {
            IsSuccess = true,
            Message = "Users with roles retrieved successfully.",
            Data = users
        };
    }

    private sealed class UserWithRolesSqlRow
    {
        public Guid Id { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public bool IsActive { get; set; }
        public string? RoleName { get; set; }
    }
}
