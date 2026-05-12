using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CustomerContacts.Queries;

/// <summary>
/// Handles customer contact detail queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create read connections.</param>
/// <param name="currentUserService">Service that exposes the active tenant identifier.</param>
public sealed class GetCustomerContactByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCustomerContactByIdQuery, Response<CustomerContactResponseDto>>
{
    /// <summary>
    /// Retrieves a single contact record restricted to the current tenant and active rows only.
    /// </summary>
    /// <param name="request">The query containing the contact identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A response containing the requested contact data.</returns>
    public async Task<Response<CustomerContactResponseDto>> Handle(
        GetCustomerContactByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CustomerContactResponseDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                Id,
                CustomerId,
                ContactType,
                ContactValue,
                IsPrimary,
                Comments,
                CompanyId
            FROM Admin.CustomerContacts
            WHERE Id = @Id
              AND CompanyId = @CompanyId
              AND GcRecord = 0;
            """;

        var contact = await connection.QuerySingleOrDefaultAsync<CustomerContactResponseDto>(
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
            throw new NotFoundException(nameof(CustomerContactResponseDto), request.Id, "Customer contact not found.");
        }

        return new Response<CustomerContactResponseDto>
        {
            IsSuccess = true,
            Message = "Customer contact retrieved successfully.",
            Data = contact
        };
    }
}
