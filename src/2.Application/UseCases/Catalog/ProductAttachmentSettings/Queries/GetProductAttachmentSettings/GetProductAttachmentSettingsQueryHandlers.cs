using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UseCases.Catalog.ProductAttachmentSettings.Commands;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.ProductAttachmentSettings.Queries;

/// <summary>Handles <see cref="GetProductAttachmentSettingsQuery"/> with Dapper.</summary>
public sealed class GetProductAttachmentSettingsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProductAttachmentSettingsQuery, Response<ProductAttachmentSettingsDto>>
{
    public async Task<Response<ProductAttachmentSettingsDto>> Handle(GetProductAttachmentSettingsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductAttachmentSettingsRules.CompanyRequired<ProductAttachmentSettingsDto>();
        }

        const string sql = """
            SELECT Id, CompanyId, AllowedDocumentTypes, MaxDocumentSizeBytes, MaxDocumentsPerProduct,
                   AllowedImageFormats, MaxImageSizeBytes, MaxImagesPerProduct, Created AS CreatedAt
            FROM catalog.productattachmentsettings
            WHERE CompanyId = @CompanyId AND GcRecord = 0;
            """;

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ProductAttachmentSettingsDto>(new CommandDefinition(
            sql, new { currentUserService.CompanyId }, cancellationToken: cancellationToken));

        return row is null
            ? Response<ProductAttachmentSettingsDto>.Error(
                CatalogErrorCodes.ProductAttachmentSettingsNotConfigured,
                ["The company has no product attachment settings."])
            : new Response<ProductAttachmentSettingsDto>
            {
                IsSuccess = true,
                Message = "Product attachment settings retrieved successfully.",
                Data = ProductAttachmentSettingsRules.WithNames(row)
            };
    }
}
