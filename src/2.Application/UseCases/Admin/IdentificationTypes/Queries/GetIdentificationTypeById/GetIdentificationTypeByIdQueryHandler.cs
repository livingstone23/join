using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IdentificationTypes.Queries;

/// <summary>
/// Handles single identification type detail queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
public sealed class GetIdentificationTypeByIdQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetIdentificationTypeByIdQuery, Response<IdentificationTypeDto>>
{
    /// <summary>
    /// Retrieves a single active identification type.
    /// </summary>
    /// <param name="request">The incoming detail query.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the requested identification type when it exists.</returns>
    public async Task<Response<IdentificationTypeDto>> Handle(GetIdentificationTypeByIdQuery request, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                it.Id,
                it.Name,
                it.Description,
                it.ValidationPattern,
                it.IsActive,
                it.Created AS CreatedAt
            FROM Admin.IdentificationTypes it
            WHERE it.Id = @Id
              AND it.GcRecord = 0;
            """;

        var entity = await connection.QuerySingleOrDefaultAsync<IdentificationTypeDto>(
            new CommandDefinition(sql, new { request.Id }, cancellationToken: cancellationToken));

        if (entity is null)
        {
            return Response<IdentificationTypeDto>.Error(
                "IDENTIFICATION_TYPE_NOT_FOUND",
                ["Identification type not found."]);
        }

        return new Response<IdentificationTypeDto>
        {
            IsSuccess = true,
            Message = "Identification type retrieved successfully.",
            Data = entity
        };
    }
}