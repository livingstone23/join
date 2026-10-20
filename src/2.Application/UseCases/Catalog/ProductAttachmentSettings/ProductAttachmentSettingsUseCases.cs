using Dapper;
using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Catalog;
using JOIN.Domain.Catalog;
using JOIN.Domain.Enums;
using MediatR;
using Settings = JOIN.Domain.Catalog.ProductAttachmentSettings;

namespace JOIN.Application.UseCases.Catalog.ProductAttachmentSettings;

public interface IProductAttachmentSettingsInput
{
    int AllowedDocumentTypes { get; }
    long MaxDocumentSizeBytes { get; }
    int MaxDocumentsPerProduct { get; }
    int AllowedImageFormats { get; }
    long MaxImageSizeBytes { get; }
    int MaxImagesPerProduct { get; }
}

public sealed record GetProductAttachmentSettingsQuery : IRequest<Response<ProductAttachmentSettingsDto>>;

public sealed record CreateProductAttachmentSettingsCommand : IProductAttachmentSettingsInput, ITransactionalCommand<Response<ProductAttachmentSettingsDto>>
{
    public int AllowedDocumentTypes { get; init; }
    public long MaxDocumentSizeBytes { get; init; }
    public int MaxDocumentsPerProduct { get; init; }
    public int AllowedImageFormats { get; init; }
    public long MaxImageSizeBytes { get; init; }
    public int MaxImagesPerProduct { get; init; }
}

public sealed record UpdateProductAttachmentSettingsCommand : IProductAttachmentSettingsInput, ITransactionalCommand<Response<ProductAttachmentSettingsDto>>
{
    public int AllowedDocumentTypes { get; init; }
    public long MaxDocumentSizeBytes { get; init; }
    public int MaxDocumentsPerProduct { get; init; }
    public int AllowedImageFormats { get; init; }
    public long MaxImageSizeBytes { get; init; }
    public int MaxImagesPerProduct { get; init; }
}

internal static class ProductAttachmentSettingsRules
{
    private const int AllDocumentTypes = (int)(DocumentType.Pdf | DocumentType.Word | DocumentType.Excel | DocumentType.Text | DocumentType.Image | DocumentType.Other);
    private const int AllImageFormats = (int)(ProductImageFormat.Jpeg | ProductImageFormat.Png | ProductImageFormat.Webp);

    internal static void Apply<T>(AbstractValidator<T> validator) where T : IProductAttachmentSettingsInput
    {
        validator.RuleFor(x => x.AllowedDocumentTypes).Must(v => v > 0 && (v & ~AllDocumentTypes) == 0).WithMessage("AllowedDocumentTypes must combine known document types.");
        validator.RuleFor(x => x.MaxDocumentSizeBytes).InclusiveBetween(1, 100L * 1024 * 1024);
        validator.RuleFor(x => x.MaxDocumentsPerProduct).InclusiveBetween(1, 1000);
        validator.RuleFor(x => x.AllowedImageFormats).Must(v => v > 0 && (v & ~AllImageFormats) == 0).WithMessage("AllowedImageFormats must combine known image formats.");
        validator.RuleFor(x => x.MaxImageSizeBytes).InclusiveBetween(1, 100L * 1024 * 1024);
        validator.RuleFor(x => x.MaxImagesPerProduct).InclusiveBetween(1, ProductImage.MaxImagesPerProduct);
    }

    internal static void Copy(Settings entity, IProductAttachmentSettingsInput input)
    {
        entity.AllowedDocumentTypes = (DocumentType)input.AllowedDocumentTypes;
        entity.MaxDocumentSizeBytes = input.MaxDocumentSizeBytes;
        entity.MaxDocumentsPerProduct = input.MaxDocumentsPerProduct;
        entity.AllowedImageFormats = (ProductImageFormat)input.AllowedImageFormats;
        entity.MaxImageSizeBytes = input.MaxImageSizeBytes;
        entity.MaxImagesPerProduct = input.MaxImagesPerProduct;
    }

    internal static ProductAttachmentSettingsDto ToDto(Settings s) => new()
    {
        Id = s.Id,
        CompanyId = s.CompanyId,
        AllowedDocumentTypes = (int)s.AllowedDocumentTypes,
        AllowedDocumentTypeNames = Enum.GetValues<DocumentType>().Where(t => t != DocumentType.None && s.AllowedDocumentTypes.HasFlag(t)).Select(t => t.ToString()).ToList(),
        MaxDocumentSizeBytes = s.MaxDocumentSizeBytes,
        MaxDocumentsPerProduct = s.MaxDocumentsPerProduct,
        AllowedImageFormats = (int)s.AllowedImageFormats,
        AllowedImageFormatNames = Enum.GetValues<ProductImageFormat>().Where(t => t != ProductImageFormat.None && s.AllowedImageFormats.HasFlag(t)).Select(t => t.ToString()).ToList(),
        MaxImageSizeBytes = s.MaxImageSizeBytes,
        MaxImagesPerProduct = s.MaxImagesPerProduct,
        CreatedAt = s.Created
    };

    internal static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
}

internal sealed class CreateProductAttachmentSettingsCommandValidator : AbstractValidator<CreateProductAttachmentSettingsCommand>
{
    public CreateProductAttachmentSettingsCommandValidator() => ProductAttachmentSettingsRules.Apply(this);
}

internal sealed class UpdateProductAttachmentSettingsCommandValidator : AbstractValidator<UpdateProductAttachmentSettingsCommand>
{
    public UpdateProductAttachmentSettingsCommandValidator() => ProductAttachmentSettingsRules.Apply(this);
}

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
            sql,
            new { CompanyId = currentUserService.CompanyId },
            cancellationToken: cancellationToken));

        return row is null
            ? Response<ProductAttachmentSettingsDto>.Error(
                CatalogErrorCodes.ProductAttachmentSettingsNotConfigured,
                ["The company has no product attachment settings."])
            : new Response<ProductAttachmentSettingsDto>
            {
                IsSuccess = true,
                Message = "Product attachment settings retrieved successfully.",
                Data = row with
                {
                    AllowedDocumentTypeNames = Enum.GetValues<DocumentType>()
                        .Where(t => t != DocumentType.None && ((DocumentType)row.AllowedDocumentTypes).HasFlag(t))
                        .Select(t => t.ToString()).ToList(),
                    AllowedImageFormatNames = Enum.GetValues<ProductImageFormat>()
                        .Where(t => t != ProductImageFormat.None && ((ProductImageFormat)row.AllowedImageFormats).HasFlag(t))
                        .Select(t => t.ToString()).ToList()
                }
            };
    }
}

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
        var exists = (await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0)).Any();
        if (exists)
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