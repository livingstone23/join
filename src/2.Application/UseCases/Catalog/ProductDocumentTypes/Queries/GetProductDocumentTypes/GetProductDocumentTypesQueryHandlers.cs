using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Services.Catalog;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.ProductDocumentTypes.Queries;

/// <summary>Handles <see cref="GetProductDocumentTypesQuery"/> with Dapper.</summary>
public sealed class GetProductDocumentTypesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProductDocumentTypesQuery, Response<IReadOnlyList<ProductDocumentTypeDto>>>
{
    public async Task<Response<IReadOnlyList<ProductDocumentTypeDto>>> Handle(GetProductDocumentTypesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyList<ProductDocumentTypeDto>>.Error(
                CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        const string sql = """
            SELECT Id, CompanyId, Code, Name, SortOrder, IsActive, Created AS CreatedAt
            FROM catalog.productdocumenttypes
            WHERE CompanyId = @CompanyId
              AND GcRecord = 0
              AND (@IncludeInactive = TRUE OR IsActive = TRUE)
            ORDER BY SortOrder, Name;
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProductDocumentTypeDto>(new CommandDefinition(
            sql,
            new { currentUserService.CompanyId, IncludeInactive = request.IncludeInactive == true },
            cancellationToken: cancellationToken));

        return new Response<IReadOnlyList<ProductDocumentTypeDto>>
        {
            IsSuccess = true,
            Message = "Product document types retrieved successfully.",
            Data = rows.AsList()
        };
    }
}
