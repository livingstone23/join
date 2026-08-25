using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Security.Users.Queries.GetUsersWithRoles;

/// <summary>
/// Handles the retrieval of the active user list together with the roles currently
/// assigned to each account inside the caller's tenant. Rewritten by SPEC 28 / F2
/// to read from <c>Security.UserRoleCompanies</c> (the table that governs
/// authorization) instead of <c>Security.UserRoles</c> (Identity) and to scope the
/// result set to users with an active membership in the tenant from the JWT
/// (SPEC 23). Without the tenant filter the endpoint was a cross-tenant leak.
/// </summary>
public sealed class GetUsersWithRolesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetUsersWithRolesQuery, Response<IEnumerable<UserWithRolesDto>>>
{
    private const string Sql = """
        SELECT
            u.Id,
            u.UserName,
            u.Email,
            u.IsActive,
            r.Name AS RoleName
        FROM [Security].[Users] u
        INNER JOIN [Security].[UserCompanies] uc
            ON uc.UserId = u.Id
           AND uc.CompanyId = @CompanyId
           AND uc.GcRecord = 0
        LEFT JOIN [Security].[UserRoleCompanies] urc
            ON urc.UserId = u.Id
           AND urc.CompanyId = @CompanyId
           AND urc.GcRecord = 0
        LEFT JOIN [Security].[Roles] r
            ON r.Id = urc.RoleId
           AND r.GcRecord = 0
        WHERE u.GcRecord = 0
        ORDER BY u.UserName, r.Name;
        """;

    public async Task<Response<IEnumerable<UserWithRolesDto>>> Handle(
        GetUsersWithRolesQuery request,
        CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<IEnumerable<UserWithRolesDto>>.Error(
                "TENANT_REQUIRED",
                ["A tenant context is required to list users."]);
        }

        using var connection = connectionFactory.CreateConnection();
        connection.Open();

        var rows = (await connection.QueryAsync<UserWithRolesSqlRow>(
            new CommandDefinition(Sql, new { CompanyId = companyId }, cancellationToken: cancellationToken)))
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