using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Genders.Queries;

/// <summary>
/// Handles tenant-scoped gender detail queries using Dapper.
/// </summary>
public sealed class GetGenderByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetGenderByIdQuery, Response<GenderDto>>
{
    /// <inheritdoc />
    public async Task<Response<GenderDto>> Handle(GetGenderByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<GenderDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                g.Id,
                g.CompanyId,
                c.Name AS CompanyName,
                g.Code,
                g.Name,
                g.IsActive,
                g.Created AS CreatedAt
            FROM Admin.Genders g
            INNER JOIN Common.Companies c
                ON c.Id = g.CompanyId
               AND c.GcRecord = 0
            WHERE g.Id = @Id
              AND g.CompanyId = @CompanyId
              AND g.GcRecord = 0;
            """;

        var gender = await connection.QuerySingleOrDefaultAsync<GenderDto>(
            new CommandDefinition(
                sql,
                new { request.Id, CompanyId = currentUserService.CompanyId },
                cancellationToken: cancellationToken));

        if (gender is null)
        {
            return Response<GenderDto>.Error("GENDER_NOT_FOUND", ["Gender not found."]);
        }

        return new Response<GenderDto>
        {
            IsSuccess = true,
            Message = "Gender retrieved successfully.",
            Data = gender
        };
    }
}
