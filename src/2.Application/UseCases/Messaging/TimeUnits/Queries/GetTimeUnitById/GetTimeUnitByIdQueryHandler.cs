using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TimeUnits.Queries;

/// <summary>
/// Handles time unit detail queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
public sealed class GetTimeUnitByIdQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetTimeUnitByIdQuery, Response<TimeUnitDto>>
{
    private readonly ICurrentUserService _currentUserService = currentUserService;
    /// <summary>
    /// Retrieves a time unit catalog item by its unique identifier.
    /// </summary>
    public async Task<Response<TimeUnitDto>> Handle(GetTimeUnitByIdQuery request, CancellationToken cancellationToken)
    {
        if (_currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TimeUnitDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                tu.Id,
                tu.CompanyId,
                c.Name AS CompanyName,
                tu.Name,
                tu.Code,
                tu.IsActive,
                tu.Created AS CreatedAt
            FROM Messaging.TimeUnits tu
            LEFT JOIN Common.Companies c ON c.Id = tu.CompanyId
            WHERE tu.Id = @Id
              AND tu.CompanyId = @TenantId
              AND tu.GcRecord = 0;
            """;

        var timeUnit = await connection.QuerySingleOrDefaultAsync<TimeUnitDto>(
            new CommandDefinition(sql, new { request.Id, TenantId = _currentUserService.CompanyId }, cancellationToken: cancellationToken));

        if (timeUnit is null)
        {
            return Response<TimeUnitDto>.Error("TIME_UNIT_NOT_FOUND", ["Time unit not found."]);
        }

        return new Response<TimeUnitDto>
        {
            IsSuccess = true,
            Message = "Time unit retrieved successfully.",
            Data = timeUnit
        };
    }
}
