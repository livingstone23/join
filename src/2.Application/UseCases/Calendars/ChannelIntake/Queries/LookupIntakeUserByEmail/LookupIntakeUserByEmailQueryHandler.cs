using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.LookupIntakeUserByEmail;

/// <summary>
/// Resolves a JOIN user by exact (case-insensitive) e-mail among the active users of the company that have a
/// <c>CalendarUserConfiguration</c> row (SPEC 45). Returns <see cref="IntakeUserLookupDto"/> with
/// <c>Found = false</c> when there is no match — the agent falls through to the company default user.
/// </summary>
public sealed class LookupIntakeUserByEmailQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<LookupIntakeUserByEmailQuery, Response<IntakeUserLookupDto>>
{
    private const string Sql = """
        SELECT u.id AS UserId, u.FirstName || ' ' || u.LastName AS FullName
          FROM Security.Users u
          INNER JOIN Security.UserCompanies uc
            ON uc.UserId = u.Id
           AND uc.CompanyId = @TenantId
           AND uc.GcRecord = 0
          INNER JOIN Calendar.UserConfigurations c
            ON c.UserId = u.Id
           AND c.CompanyId = @TenantId
           AND c.GcRecord = 0
         WHERE u.GcRecord = 0
           AND u.IsActive = TRUE
           AND u.Email IS NOT NULL
           AND LOWER(TRIM(u.Email)) = LOWER(TRIM(@Email))
         LIMIT 1;
        """;

    public async Task<Response<IntakeUserLookupDto>> Handle(LookupIntakeUserByEmailQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IntakeUserLookupDto>.Error(CalendarErrorCodes.CompanyRequired,
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return new Response<IntakeUserLookupDto>
            {
                IsSuccess = true,
                Data = new IntakeUserLookupDto(false, null, null),
                Message = "Empty email; no user matched."
            };
        }

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<UserRow>(
            new CommandDefinition(Sql, new { TenantId = currentUserService.CompanyId, Email = request.Email.Trim() }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return new Response<IntakeUserLookupDto>
            {
                IsSuccess = true,
                Data = new IntakeUserLookupDto(false, null, null),
                Message = "No matching user; falls through to the company default user."
            };
        }

        return new Response<IntakeUserLookupDto>
        {
            IsSuccess = true,
            Data = new IntakeUserLookupDto(true, row.UserId, row.FullName.Trim()),
            Message = "User matched."
        };
    }

    private sealed class UserRow
    {
        public Guid UserId { get; init; }
        public string FullName { get; init; } = string.Empty;
    }
}