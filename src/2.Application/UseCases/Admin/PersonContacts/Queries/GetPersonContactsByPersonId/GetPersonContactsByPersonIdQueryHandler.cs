using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;



namespace JOIN.Application.UseCases.Admin.PersonContacts.Queries;



/// <summary>
/// Handles person contact listing requests using Dapper for read-optimized access.
/// </summary>
public sealed class GetPersonContactsByPersonIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonContactsByPersonIdQuery, Response<List<PersonContactResponseDto>>>
{
    /// <summary>
    /// Retrieves person contacts from SQL using tenant and soft-delete filters.
    /// </summary>
    /// <param name="request">The request carrying the person identifier.</param>
    /// <param name="cancellationToken">A cancellation token for query execution.</param>
    /// <returns>A response containing the list of person contacts.</returns>
    public async Task<Response<List<PersonContactResponseDto>>> Handle(
        GetPersonContactsByPersonIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<List<PersonContactResponseDto>>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                c.Id,
                c.PersonId,
                c.ContactType,
                c.ContactValue,
                c.IsPrimary,
                c.Comments,
                c.CompanyId
            FROM Admin.PersonContacts c
            WHERE c.PersonId = @PersonId
              AND c.CompanyId = @CompanyId
              AND c.GcRecord = 0
            ORDER BY c.IsPrimary DESC, c.Created DESC;
            """;

        var rows = await connection.QueryAsync<PersonContactResponseDto>(
            new CommandDefinition(
                sql,
                new
                {
                    request.PersonId,
                    CompanyId = currentUserService.CompanyId
                },
                cancellationToken: cancellationToken));

        return new Response<List<PersonContactResponseDto>>
        {
            IsSuccess = true,
            Message = "Person contacts retrieved successfully.",
            Data = rows.ToList()
        };
    }
}
