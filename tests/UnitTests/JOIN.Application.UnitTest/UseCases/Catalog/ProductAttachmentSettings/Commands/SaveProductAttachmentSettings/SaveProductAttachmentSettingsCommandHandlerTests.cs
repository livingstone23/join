using FluentAssertions;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UnitTest.UseCases.Catalog.Products;
using JOIN.Application.UseCases.Catalog.ProductAttachmentSettings.Commands;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.UseCases.Catalog.ProductAttachmentSettings.Commands.SaveProductAttachmentSettings;

/// <summary>SPEC 70 B.14 — one live row of limits per company: POST (409 when it exists) and PUT.</summary>
public sealed class SaveProductAttachmentSettingsCommandHandlerTests
{
    private static CreateProductAttachmentSettingsCommand Create() => new()
    {
        AllowedDocumentTypes = (int)(DocumentType.Pdf | DocumentType.Word),
        MaxDocumentSizeBytes = 4_000_000,
        MaxDocumentsPerProduct = 10,
        AllowedImageFormats = (int)(ProductImageFormat.Jpeg | ProductImageFormat.Png),
        MaxImageSizeBytes = 2_000_000,
        MaxImagesPerProduct = 3
    };

    [Fact]
    public async Task Create_InsertsTheRow_WithTheFlagNames_ThenASecondOneConflicts()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync(withSettings: false);

        var created = await bed.Context.SendAsync(Create());

        created.IsSuccess.Should().BeTrue(created.Message);
        created.Data!.MaxImagesPerProduct.Should().Be(3);
        created.Data.AllowedDocumentTypeNames.Should().BeEquivalentTo(["Pdf", "Word"]);
        created.Data.AllowedImageFormatNames.Should().BeEquivalentTo(["Jpeg", "Png"]);
        (await bed.Context.SendAsync(Create())).Message.Should().Be(CatalogErrorCodes.ProductAttachmentSettingsAlreadyExists);
    }

    [Fact]
    public async Task Update_ChangesTheRow_OrRespondsNotConfigured()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();

        var updated = await bed.Context.SendAsync(new UpdateProductAttachmentSettingsCommand
        {
            AllowedDocumentTypes = (int)DocumentType.Pdf, MaxDocumentSizeBytes = 1, MaxDocumentsPerProduct = 1,
            AllowedImageFormats = (int)ProductImageFormat.Webp, MaxImageSizeBytes = 1, MaxImagesPerProduct = 1
        });

        updated.IsSuccess.Should().BeTrue(updated.Message);
        (await bed.SettingsAsync()).AllowedImageFormats.Should().Be(ProductImageFormat.Webp);

        using var empty = await ProductAttachmentTestBed.CreateAsync(withSettings: false);
        (await empty.Context.SendAsync(new UpdateProductAttachmentSettingsCommand())).Message.Should().Be(CatalogErrorCodes.ProductAttachmentSettingsNotConfigured);
    }

    [Fact]
    public async Task WithoutCompany_RespondsCompanyRequired()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        bed.Context.ActingCompanyId = Guid.Empty;

        (await bed.Context.SendAsync(Create())).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await bed.Context.SendAsync(new UpdateProductAttachmentSettingsCommand())).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
    }

    [Fact]
    public void Validators_KnownFlagsPositiveSizesAndOneToFiveImages()
    {
        var create = new CreateProductAttachmentSettingsCommandValidator();
        create.Validate(Create()).IsValid.Should().BeTrue();
        create.Validate(Create() with { MaxImagesPerProduct = 6 }).IsValid.Should().BeFalse();
        create.Validate(Create() with { MaxImagesPerProduct = 0 }).IsValid.Should().BeFalse();
        create.Validate(Create() with { AllowedImageFormats = 8 }).IsValid.Should().BeFalse();
        create.Validate(Create() with { AllowedDocumentTypes = 0 }).IsValid.Should().BeFalse();
        create.Validate(Create() with { MaxDocumentSizeBytes = 0, MaxImageSizeBytes = 0, MaxDocumentsPerProduct = 0 }).Errors.Should().HaveCount(3);
        new UpdateProductAttachmentSettingsCommandValidator().Validate(new UpdateProductAttachmentSettingsCommand()).IsValid.Should().BeFalse();
    }
}
