using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.EntityStatuses.Queries;

/// <summary>
/// Handles single entity status detail queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
public sealed class GetEntityStatusByIdQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetEntityStatusByIdQuery, Response<EntityStatusDto>>
{
    /// <summary>
    /// Retrieves a single active entity status.
    /// </summary>
    /// <param name="request">The incoming detail query.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the requested entity status when it exists.</returns>
    public async Task<Response<EntityStatusDto>> Handle(GetEntityStatusByIdQuery request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<EntityStatusDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string companyValidationSql = """
            SELECT COUNT(1)
            FROM Common.Companies c
            WHERE c.Id = @CompanyId
              AND c.GcRecord = 0;
            """;

        var companyExists = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(companyValidationSql, new { request.CompanyId }, cancellationToken: cancellationToken));

        if (companyExists == 0)
        {
            return Response<EntityStatusDto>.Error(
                "INVALID_COMPANY_ID",
                ["The specified CompanyId does not exist."]);
        }

        const string sql = """
            SELECT
                es.Id,
                es.Name,
                es.Description,
                es.Code,
                es.IsOperative,
                es.Created AS CreatedAt
            FROM Admin.EntityStatuses es
            WHERE es.Id = @Id
              AND es.GcRecord = 0;
            """;

        var entity = await connection.QuerySingleOrDefaultAsync<EntityStatusDto>(
            new CommandDefinition(sql, new { request.Id }, cancellationToken: cancellationToken));

        if (entity is null)
        {
            return Response<EntityStatusDto>.Error(
                "ENTITY_STATUS_NOT_FOUND",
                ["Entity status not found."]);
        }

        return new Response<EntityStatusDto>
        {
            IsSuccess = true,
            Message = "Entity status retrieved successfully.",
            Data = entity
        };
    }
}
