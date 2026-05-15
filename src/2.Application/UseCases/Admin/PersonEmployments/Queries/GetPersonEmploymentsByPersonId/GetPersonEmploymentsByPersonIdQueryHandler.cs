using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Queries;

/// <summary>
/// Handles person employment listing requests using Dapper for read-optimized access.
/// </summary>
public sealed class GetPersonEmploymentsByPersonIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonEmploymentsByPersonIdQuery, Response<List<PersonEmploymentResponseDto>>>
{
    /// <summary>
    /// Retrieves person employment records from SQL using tenant and soft-delete filters.
    /// </summary>
    /// <param name="request">The request carrying the person identifier.</param>
    /// <param name="cancellationToken">A cancellation token for query execution.</param>
    /// <returns>A response containing the list of person employment records.</returns>
    public async Task<Response<List<PersonEmploymentResponseDto>>> Handle(
        GetPersonEmploymentsByPersonIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<List<PersonEmploymentResponseDto>>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                Id,
                PersonId,
                EmployerName,
                JobTitle,
                StartDate,
                EndDate,
                IsCurrent,
                IsActive
            FROM Admin.PersonEmployments
            WHERE PersonId = @PersonId
              AND CompanyId = @CompanyId
              AND GcRecord = 0
            ORDER BY IsCurrent DESC, StartDate DESC;
            """;

        var rows = await connection.QueryAsync<PersonEmploymentResponseDto>(
            new CommandDefinition(
                sql,
                new
                {
                    request.PersonId,
                    CompanyId = currentUserService.CompanyId
                },
                cancellationToken: cancellationToken));

        return new Response<List<PersonEmploymentResponseDto>>
        {
            IsSuccess = true,
            Message = "Person employments retrieved successfully.",
            Data = rows.ToList()
        };
    }
}
