using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Projects.Queries;

/// <summary>
/// Handles tenant-scoped project detail queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
public sealed class GetProjectByIdQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetProjectByIdQuery, Response<ProjectDto>>
{
    /// <summary>
    /// Retrieves a single active project that belongs to the requested company.
    /// </summary>
    /// <param name="request">The tenant-scoped detail query.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the requested project when it exists.</returns>
    public async Task<Response<ProjectDto>> Handle(GetProjectByIdQuery request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<ProjectDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                p.Id,
                p.CompanyId,
                c.Name AS CompanyName,
                p.Name,
                p.EntityStatusId,
                es.Name AS EntityStatusName,
                p.Created AS CreatedAt
            FROM Admin.Projects p
            INNER JOIN Admin.EntityStatuses es
                ON es.Id = p.EntityStatusId
               AND es.GcRecord = 0
            INNER JOIN Common.Companies c
                ON c.Id = p.CompanyId
               AND c.GcRecord = 0
            WHERE p.Id = @Id
              AND p.CompanyId = @CompanyId
              AND p.GcRecord = 0;
            """;

        var project = await connection.QuerySingleOrDefaultAsync<ProjectDto>(
            new CommandDefinition(sql, new { request.Id, request.CompanyId }, cancellationToken: cancellationToken));

        if (project is null)
        {
            return Response<ProjectDto>.Error("PROJECT_NOT_FOUND", ["Project not found."]);
        }

        return new Response<ProjectDto>
        {
            IsSuccess = true,
            Message = "Project retrieved successfully.",
            Data = project
        };
    }
}