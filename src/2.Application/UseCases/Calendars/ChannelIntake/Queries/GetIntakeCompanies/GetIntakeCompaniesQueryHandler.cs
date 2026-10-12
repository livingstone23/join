using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.GetIntakeCompanies;

/// <summary>
/// Reads the companies the calling agent has access to. The query runs WITHOUT a tenant filter — the agent is
/// choosing the company. A role check enforces <c>Agent</c> or <c>SuperAdmin</c> (<c>403</c> otherwise).
/// </summary>
public sealed class GetIntakeCompaniesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetIntakeCompaniesQuery, Response<IReadOnlyList<IntakeCompanyDto>>>
{
    private const string Sql = """
        SELECT co.Id AS CompanyId, co.Name, co.TaxId
          FROM Common.Companies co
          INNER JOIN Security.UserCompanies uc
            ON uc.CompanyId = co.Id
           AND uc.UserId = @UserId
           AND uc.GcRecord = 0
          INNER JOIN Admin.CompanyModules cm
            ON cm.CompanyId = co.Id
           AND cm.IsActive = TRUE
           AND cm.GcRecord = 0
          INNER JOIN Admin.SystemModules m
            ON m.Id = cm.ModuleId
           AND m.IsActive = TRUE
           AND m.GcRecord = 0
           AND m.Name = @ModuleName
          INNER JOIN Calendar.CalendarCompanies cc
            ON cc.CompanyId = co.Id
           AND cc.GcRecord = 0
         WHERE co.GcRecord = 0
           AND (LOWER(co.Name) LIKE @Search OR LOWER(co.TaxId) LIKE @Search)
         ORDER BY co.Name;
        """;

    public async Task<Response<IReadOnlyList<IntakeCompanyDto>>> Handle(GetIntakeCompaniesQuery request, CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated
            || !(currentUserService.IsInRole("Agent") || currentUserService.IsInRole("SuperAdmin")))
        {
            return Response<IReadOnlyList<IntakeCompanyDto>>.Error(CalendarErrorCodes.IntakeCompanyNotAllowed,
                ["Only Agent or SuperAdmin users can list the intake companies."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var userId) || userId == Guid.Empty)
        {
            return Response<IReadOnlyList<IntakeCompanyDto>>.Error(CalendarErrorCodes.UserRequired,
                ["The authenticated user could not be resolved."]);
        }

        var search = $"%{request.Search?.Trim().ToLowerInvariant() ?? string.Empty}%";

        using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<CompanyRow>(
            new CommandDefinition(Sql, new { UserId = userId, Search = search, ModuleName = CalendarUserProvisioner.CalendarModuleName }, cancellationToken: cancellationToken)))
            .Select(r => new IntakeCompanyDto(r.CompanyId, r.Name, r.TaxId))
            .ToList();

        return new Response<IReadOnlyList<IntakeCompanyDto>>
        {
            IsSuccess = true,
            Data = rows,
            Message = "Intake companies retrieved successfully."
        };
    }

    private sealed class CompanyRow
    {
        public Guid CompanyId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string TaxId { get; init; } = string.Empty;
    }
}
