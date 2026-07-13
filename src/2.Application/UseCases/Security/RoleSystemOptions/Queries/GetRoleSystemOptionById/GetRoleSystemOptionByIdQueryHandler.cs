using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;

/// <summary>
/// Handles tenant-scoped retrieval of a single RoleSystemOption rule.
/// </summary>
public sealed class GetRoleSystemOptionByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetRoleSystemOptionByIdQuery, Response<RoleSystemOptionDto>>
{
    public async Task<Response<RoleSystemOptionDto>> Handle(
        GetRoleSystemOptionByIdQuery request,
        CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<RoleSystemOptionDto>.Error(
                "INVALID_COMPANY_ID",
                ["A valid company context is required."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string whereClause = """
            WHERE rso.Id = @Id
              AND rso.CompanyId = @CompanyId
              AND rso.GcRecord = 0
            """;

        var roleSystemOption = await connection.QuerySingleOrDefaultAsync<RoleSystemOptionDto>(
            new CommandDefinition(
                RoleSystemOptionQuerySql.BuildByIdSql(whereClause),
                new { request.Id, CompanyId = companyId },
                cancellationToken: cancellationToken));

        if (roleSystemOption is null)
        {
            return Response<RoleSystemOptionDto>.Error(
                "ROLE_SYSTEM_OPTION_NOT_FOUND",
                ["Role system option not found."]);
        }

        return new Response<RoleSystemOptionDto>
        {
            IsSuccess = true,
            Message = "Role system option retrieved successfully.",
            Data = roleSystemOption
        };
    }
}
