using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.SystemModules.Queries;

/// <summary>
/// Handles single system module detail queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
public sealed class GetSystemModuleByIdQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetSystemModuleByIdQuery, Response<SystemModuleDto>>
{
    /// <summary>
    /// Retrieves a single active system module.
    /// </summary>
    /// <param name="request">The incoming detail query.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the requested system module when it exists.</returns>
    public async Task<Response<SystemModuleDto>> Handle(GetSystemModuleByIdQuery request, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                sm.Id,
                sm.Name,
                sm.Description,
                sm.Icon,
                sm.IsActive,
                sm.Created AS CreatedAt
            FROM Admin.SystemModules sm
            WHERE sm.Id = @Id
              AND sm.GcRecord = 0;
            """;

        var entity = await connection.QuerySingleOrDefaultAsync<SystemModuleDto>(
            new CommandDefinition(sql, new { request.Id }, cancellationToken: cancellationToken));

        if (entity is null)
        {
            return Response<SystemModuleDto>.Error(
                "SYSTEM_MODULE_NOT_FOUND",
                ["System module not found."]);
        }

        return new Response<SystemModuleDto>
        {
            IsSuccess = true,
            Message = "System module retrieved successfully.",
            Data = entity
        };
    }
}