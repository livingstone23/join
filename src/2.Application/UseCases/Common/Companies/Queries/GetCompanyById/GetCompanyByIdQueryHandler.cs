using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Common.Companies.Queries;

/// <summary>
/// Handles company detail queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create DB-agnostic read connections.</param>
public class GetCompanyByIdQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetCompanyByIdQuery, Response<CompanyDto>>
{
    /// <summary>
    /// Retrieves a company by id.
    /// </summary>
    public async Task<Response<CompanyDto>> Handle(GetCompanyByIdQuery request, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                c.Id,
                c.Name,
                c.Description,
                c.TaxId,
                c.Email,
                c.Phone,
                c.WebSite,
                c.IsActive
            FROM Common.Companies c
            WHERE c.Id = @Id AND c.GcRecord = 0;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("Id", request.CompanyId);

        var company = await connection.QuerySingleOrDefaultAsync<CompanyDto>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        if (company is null)
        {
            return Response<CompanyDto>.Error("COMPANY_NOT_FOUND", ["Company not found."]);
        }

        return new Response<CompanyDto>
        {
            IsSuccess = true,
            Message = "Company retrieved successfully.",
            Data = company
        };
    }
}
