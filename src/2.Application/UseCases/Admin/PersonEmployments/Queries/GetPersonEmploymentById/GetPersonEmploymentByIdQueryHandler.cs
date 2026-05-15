using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Queries;

/// <summary>
/// Handles person employment detail queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create read connections.</param>
/// <param name="currentUserService">Service that exposes the active tenant identifier.</param>
public sealed class GetPersonEmploymentByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonEmploymentByIdQuery, Response<PersonEmploymentResponseDto>>
{
    /// <summary>
    /// Retrieves a single employment record restricted to the current tenant and active rows only.
    /// </summary>
    /// <param name="request">The query containing the employment identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A response containing the requested employment data.</returns>
    public async Task<Response<PersonEmploymentResponseDto>> Handle(
        GetPersonEmploymentByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PersonEmploymentResponseDto>.Error(
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
            WHERE Id = @Id
              AND CompanyId = @CompanyId
              AND GcRecord = 0;
            """;

        var employment = await connection.QuerySingleOrDefaultAsync<PersonEmploymentResponseDto>(
            new CommandDefinition(
                sql,
                new
                {
                    request.Id,
                    CompanyId = currentUserService.CompanyId
                },
                cancellationToken: cancellationToken));

        if (employment is null)
        {
            return Response<PersonEmploymentResponseDto>.Error(
                "PERSON_EMPLOYMENT_NOT_FOUND",
                ["Person employment not found."]);
        }

        return new Response<PersonEmploymentResponseDto>
        {
            IsSuccess = true,
            Message = "Person employment retrieved successfully.",
            Data = employment
        };
    }
}
