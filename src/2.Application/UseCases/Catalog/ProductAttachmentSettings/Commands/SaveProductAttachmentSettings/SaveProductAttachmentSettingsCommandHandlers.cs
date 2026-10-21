using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Catalog;
using JOIN.Domain.Enums;
using MediatR;
using Settings = JOIN.Domain.Catalog.ProductAttachmentSettings;

namespace JOIN.Application.UseCases.Catalog.ProductAttachmentSettings.Commands;

/// <summary>Handles <see cref="CreateProductAttachmentSettingsCommand"/>: one live row per company.</summary>
public sealed class CreateProductAttachmentSettingsCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateProductAttachmentSettingsCommand, Response<ProductAttachmentSettingsDto>>
{
    public async Task<Response<ProductAttachmentSettingsDto>> Handle(CreateProductAttachmentSettingsCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductAttachmentSettingsRules.CompanyRequired<ProductAttachmentSettingsDto>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<Settings>();
        if ((await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0)).Any())
        {
            return Response<ProductAttachmentSettingsDto>.Error(
                CatalogErrorCodes.ProductAttachmentSettingsAlreadyExists,
                ["An active product attachment settings row already exists for this company."]);
        }

        var entity = new Settings { CompanyId = companyId };
        ProductAttachmentSettingsRules.Copy(entity, request);
        await repository.InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ProductAttachmentSettingsDto>
        {
            IsSuccess = true,
            Message = "Product attachment settings created successfully.",
            Data = ProductAttachmentSettingsRules.ToDto(entity)
        };
    }
}

/// <summary>Handles <see cref="UpdateProductAttachmentSettingsCommand"/>.</summary>
public sealed class UpdateProductAttachmentSettingsCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateProductAttachmentSettingsCommand, Response<ProductAttachmentSettingsDto>>
{
    public async Task<Response<ProductAttachmentSettingsDto>> Handle(UpdateProductAttachmentSettingsCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductAttachmentSettingsRules.CompanyRequired<ProductAttachmentSettingsDto>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<Settings>();
        var entity = (await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0)).FirstOrDefault();
        if (entity is null)
        {
            return Response<ProductAttachmentSettingsDto>.Error(
                CatalogErrorCodes.ProductAttachmentSettingsNotConfigured,
                ["The company has no product attachment settings row yet; create it first."]);
        }

        ProductAttachmentSettingsRules.Copy(entity, request);
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ProductAttachmentSettingsDto>
        {
            IsSuccess = true,
            Message = "Product attachment settings updated successfully.",
            Data = ProductAttachmentSettingsRules.ToDto(entity)
        };
    }
}

/// <summary>Mapping shared by the settings handlers and the query.</summary>
internal static class ProductAttachmentSettingsRules
{
    internal static void Copy(Settings entity, IProductAttachmentSettingsInput input)
    {
        entity.AllowedDocumentTypes = (DocumentType)input.AllowedDocumentTypes;
        entity.MaxDocumentSizeBytes = input.MaxDocumentSizeBytes;
        entity.MaxDocumentsPerProduct = input.MaxDocumentsPerProduct;
        entity.AllowedImageFormats = (ProductImageFormat)input.AllowedImageFormats;
        entity.MaxImageSizeBytes = input.MaxImageSizeBytes;
        entity.MaxImagesPerProduct = input.MaxImagesPerProduct;
    }

    internal static ProductAttachmentSettingsDto ToDto(Settings s) => WithNames(new ProductAttachmentSettingsDto
    {
        Id = s.Id,
        CompanyId = s.CompanyId,
        AllowedDocumentTypes = (int)s.AllowedDocumentTypes,
        MaxDocumentSizeBytes = s.MaxDocumentSizeBytes,
        MaxDocumentsPerProduct = s.MaxDocumentsPerProduct,
        AllowedImageFormats = (int)s.AllowedImageFormats,
        MaxImageSizeBytes = s.MaxImageSizeBytes,
        MaxImagesPerProduct = s.MaxImagesPerProduct,
        CreatedAt = s.Created
    });

    /// <summary>Fills the flag names the UI renders as checkboxes.</summary>
    internal static ProductAttachmentSettingsDto WithNames(ProductAttachmentSettingsDto dto) => dto with
    {
        AllowedDocumentTypeNames = Enum.GetValues<DocumentType>()
            .Where(t => t != DocumentType.None && ((DocumentType)dto.AllowedDocumentTypes).HasFlag(t))
            .Select(t => t.ToString()).ToList(),
        AllowedImageFormatNames = Enum.GetValues<ProductImageFormat>()
            .Where(t => t != ProductImageFormat.None && ((ProductImageFormat)dto.AllowedImageFormats).HasFlag(t))
            .Select(t => t.ToString()).ToList()
    };

    internal static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
}
