using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using MediatR;



namespace JOIN.Application.UseCases.Admin.PersonContacts.Queries;



/// <summary>
/// Handles person contact detail queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create read connections.</param>
/// <param name="currentUserService">Service that exposes the active tenant identifier.</param>
public sealed class GetPersonContactByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonContactByIdQuery, Response<PersonContactResponseDto>>
{
    /// <summary>
    /// Retrieves a single person contact record restricted to the current tenant and active rows only.
    /// </summary>
    /// <param name="request">The query containing the contact identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A response containing the requested contact data.</returns>
    public async Task<Response<PersonContactResponseDto>> Handle(
        GetPersonContactByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PersonContactResponseDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                Id,
                PersonId,
                ContactType,
                ContactValue,
                IsPrimary,
                Comments,
                CompanyId
            FROM Admin.PersonContacts
            WHERE Id = @Id
              AND CompanyId = @CompanyId
              AND GcRecord = 0;
            """;

        var contact = await connection.QuerySingleOrDefaultAsync<PersonContactResponseDto>(
            new CommandDefinition(
                sql,
                new
                {
                    request.Id,
                    CompanyId = currentUserService.CompanyId
                },
                cancellationToken: cancellationToken));

        if (contact is null)
        {
            throw new NotFoundException(nameof(PersonContactResponseDto), request.Id, "Person contact not found.");
        }

        return new Response<PersonContactResponseDto>
        {
            IsSuccess = true,
            Message = "Person contact retrieved successfully.",
            Data = contact
        };
    }
}
