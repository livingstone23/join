using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.SalesTeams;
using JOIN.Application.Interface;
using JOIN.Application.Services.Customers;
using MediatR;

namespace JOIN.Application.UseCases.Customers.SalesTeams.Teams.Queries;

/// <summary>Shared SQL of the sales team queries. <c>@Today</c> decides which memberships are current.</summary>
internal static class SalesTeamQuerySql
{
    internal const string CurrentCondition = "m.ValidFrom <= @Today AND (m.ValidTo IS NULL OR m.ValidTo >= @Today)";

    internal const string SelectTeam = $"""
        SELECT
            t.Id,
            t.GcRecord,
            t.CompanyId,
            t.Name,
            t.Description,
            t.IsActive,
            t.Created AS CreatedAt,
            (SELECT COUNT(*)::int FROM customer.salesteammembers m
              WHERE m.SalesTeamId = t.Id AND m.GcRecord = 0 AND {CurrentCondition}) AS CurrentMembersCount,
            leader.UserId AS LeaderUserId,
            leader.UserName AS LeaderName
        FROM customer.salesteams t
        LEFT JOIN LATERAL (
            SELECT m.UserId, CONCAT(u.FirstName, ' ', u.LastName) AS UserName
            FROM customer.salesteammembers m
            INNER JOIN customer.teamroles r ON r.Id = m.TeamRoleId AND r.IsLeader = TRUE
            INNER JOIN security.users u ON u.Id = m.UserId
            WHERE m.SalesTeamId = t.Id AND m.GcRecord = 0 AND {CurrentCondition}
            ORDER BY m.ValidFrom
            LIMIT 1
        ) leader ON TRUE
        """;

    internal const string SelectMember = $"""
        SELECT
            m.Id,
            m.SalesTeamId,
            t.Name AS SalesTeamName,
            t.IsActive AS SalesTeamIsActive,
            m.UserId,
            CONCAT(u.FirstName, ' ', u.LastName) AS UserName,
            u.Email,
            m.TeamRoleId,
            r.Code AS TeamRoleCode,
            r.Name AS TeamRoleName,
            r.LeadScope,
            r.IsLeader,
            r.CanReassign,
            m.IsPrimary,
            m.ValidFrom,
            m.ValidTo,
            ({CurrentCondition}) AS IsCurrent
        FROM customer.salesteammembers m
        INNER JOIN customer.salesteams t ON t.Id = m.SalesTeamId
        INNER JOIN customer.teamroles r ON r.Id = m.TeamRoleId
        INNER JOIN security.users u ON u.Id = m.UserId
        """;

    internal static DateTime Today => DateOnly.FromDateTime(DateTime.UtcNow).ToDateTime(TimeOnly.MinValue);

    internal static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(SalesTeamErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
}

/// <summary>Handles <see cref="GetSalesTeamsQuery"/>.</summary>
public sealed class GetSalesTeamsQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetSalesTeamsQuery, Response<IReadOnlyList<SalesTeamDto>>>
{
    public async Task<Response<IReadOnlyList<SalesTeamDto>>> Handle(GetSalesTeamsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return SalesTeamQuerySql.CompanyRequired<IReadOnlyList<SalesTeamDto>>();
        }

        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted) ? string.Empty : "AND t.GcRecord = 0";
        var inactive = request.IncludeInactive == false ? "AND t.IsActive = TRUE" : string.Empty;
        var sql = $"""
            {SalesTeamQuerySql.SelectTeam}
            WHERE t.CompanyId = @CompanyId
              {deleted}
              {inactive}
            ORDER BY t.Name;
            """;

        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<SalesTeamDto>(new CommandDefinition(
                sql,
                new { CompanyId = TenantResolver.Resolve(currentUserService, request.CompanyId), SalesTeamQuerySql.Today },
                cancellationToken: cancellationToken)))
            .ToList();

        return new Response<IReadOnlyList<SalesTeamDto>> { IsSuccess = true, Message = "Sales teams retrieved successfully.", Data = items };
    }
}

/// <summary>Handles <see cref="GetSalesTeamByIdQuery"/>.</summary>
public sealed class GetSalesTeamByIdQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetSalesTeamByIdQuery, Response<SalesTeamDetailDto>>
{
    public async Task<Response<SalesTeamDetailDto>> Handle(GetSalesTeamByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return SalesTeamQuerySql.CompanyRequired<SalesTeamDetailDto>();
        }

        var companyId = TenantResolver.Resolve(currentUserService, request.CompanyId);
        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted) ? string.Empty : "AND t.GcRecord = 0";
        var teamSql = $"""
            {SalesTeamQuerySql.SelectTeam}
            WHERE t.Id = @Id
              AND t.CompanyId = @CompanyId
              {deleted};
            """;
        var membersSql = $"""
            {SalesTeamQuerySql.SelectMember}
            WHERE m.SalesTeamId = @Id
              AND m.CompanyId = @CompanyId
              AND m.GcRecord = 0
            ORDER BY (m.ValidTo IS NULL) DESC, m.ValidFrom DESC, UserName;
            """;

        using var connection = connectionFactory.CreateConnection();
        var parameters = new { request.Id, CompanyId = companyId, SalesTeamQuerySql.Today };
        var team = await connection.QuerySingleOrDefaultAsync<SalesTeamDetailDto>(new CommandDefinition(teamSql, parameters, cancellationToken: cancellationToken));
        if (team is null)
        {
            return Response<SalesTeamDetailDto>.Error(SalesTeamErrorCodes.SalesTeamNotFound, ["Sales team not found."]);
        }

        var members = (await connection.QueryAsync<SalesTeamMemberDto>(new CommandDefinition(membersSql, parameters, cancellationToken: cancellationToken))).ToList();
        return new Response<SalesTeamDetailDto> { IsSuccess = true, Message = "Sales team retrieved successfully.", Data = team with { Members = members } };
    }
}

/// <summary>Handles <see cref="GetSalesTeamMembersQuery"/>.</summary>
public sealed class GetSalesTeamMembersQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetSalesTeamMembersQuery, Response<IReadOnlyList<SalesTeamMemberDto>>>
{
    public async Task<Response<IReadOnlyList<SalesTeamMemberDto>>> Handle(GetSalesTeamMembersQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return SalesTeamQuerySql.CompanyRequired<IReadOnlyList<SalesTeamMemberDto>>();
        }

        var current = request.CurrentOnly == true ? $"AND {SalesTeamQuerySql.CurrentCondition}" : string.Empty;
        var sql = $"""
            {SalesTeamQuerySql.SelectMember}
            WHERE m.SalesTeamId = @SalesTeamId
              AND m.CompanyId = @CompanyId
              AND m.GcRecord = 0
              AND t.GcRecord = 0
              {current}
            ORDER BY (m.ValidTo IS NULL) DESC, m.ValidFrom DESC, UserName;
            """;

        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<SalesTeamMemberDto>(new CommandDefinition(
                sql,
                new { request.SalesTeamId, CompanyId = TenantResolver.Resolve(currentUserService, request.CompanyId), SalesTeamQuerySql.Today },
                cancellationToken: cancellationToken)))
            .ToList();

        return new Response<IReadOnlyList<SalesTeamMemberDto>> { IsSuccess = true, Message = "Sales team members retrieved successfully.", Data = items };
    }
}

/// <summary>Handles <see cref="GetMySalesTeamsQuery"/>.</summary>
public sealed class GetMySalesTeamsQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetMySalesTeamsQuery, Response<IReadOnlyList<SalesTeamMemberDto>>>
{
    public async Task<Response<IReadOnlyList<SalesTeamMemberDto>>> Handle(GetMySalesTeamsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return SalesTeamQuerySql.CompanyRequired<IReadOnlyList<SalesTeamMemberDto>>();
        }

        if (!Guid.TryParse(currentUserService.UserId, out var userId))
        {
            return Response<IReadOnlyList<SalesTeamMemberDto>>.Error(SalesTeamErrorCodes.UserRequired, ["The authenticated token must contain a valid user id."]);
        }

        var sql = $"""
            {SalesTeamQuerySql.SelectMember}
            WHERE m.CompanyId = @CompanyId
              AND m.UserId = @UserId
              AND m.GcRecord = 0
              AND t.GcRecord = 0
              AND {SalesTeamQuerySql.CurrentCondition}
            ORDER BY m.IsPrimary DESC, t.Name;
            """;

        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<SalesTeamMemberDto>(new CommandDefinition(
                sql, new { currentUserService.CompanyId, UserId = userId, SalesTeamQuerySql.Today }, cancellationToken: cancellationToken)))
            .ToList();

        return new Response<IReadOnlyList<SalesTeamMemberDto>> { IsSuccess = true, Message = "Your sales teams retrieved successfully.", Data = items };
    }
}
