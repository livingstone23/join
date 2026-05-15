using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Industries.Queries;

/// <summary>
/// Handles tenant-scoped industry detail queries using Dapper.
/// </summary>
public sealed class GetIndustryByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetIndustryByIdQuery, Response<IndustryDto>>
{
    /// <inheritdoc />
    public async Task<Response<IndustryDto>> Handle(GetIndustryByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IndustryDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                i.Id,
                i.CompanyId,
                c.Name AS CompanyName,
                i.Code,
                i.Name,
                i.Description,
                i.IsActive,
                i.Created AS CreatedAt
            FROM Admin.Industries i
            INNER JOIN Common.Companies c
                ON c.Id = i.CompanyId
               AND c.GcRecord = 0
            WHERE i.Id = @Id
              AND i.CompanyId = @CompanyId
              AND i.GcRecord = 0;
            """;

        var industry = await connection.QuerySingleOrDefaultAsync<IndustryDto>(
            new CommandDefinition(
                sql,
                new { request.Id, CompanyId = currentUserService.CompanyId },
                cancellationToken: cancellationToken));

        if (industry is null)
        {
            return Response<IndustryDto>.Error("INDUSTRY_NOT_FOUND", ["Industry not found."]);
        }

        return new Response<IndustryDto>
        {
            IsSuccess = true,
            Message = "Industry retrieved successfully.",
            Data = industry
        };
    }
}
