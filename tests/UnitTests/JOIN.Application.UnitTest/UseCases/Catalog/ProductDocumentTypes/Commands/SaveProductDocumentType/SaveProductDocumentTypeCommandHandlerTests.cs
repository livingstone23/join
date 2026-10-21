using FluentAssertions;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UnitTest.UseCases.Catalog.Products;
using JOIN.Application.UseCases.Catalog.ProductDocumentTypes.Commands;
using JOIN.Domain.Catalog;

namespace JOIN.Application.UnitTest.UseCases.Catalog.ProductDocumentTypes.Commands.SaveProductDocumentType;

/// <summary>SPEC 70 B.13 — CRUD of the company's document types, unique normalized code, blocked delete while in use.</summary>
public sealed class SaveProductDocumentTypeCommandHandlerTests
{
    [Fact]
    public async Task Create_NormalizesTheCode_AndRejectsADuplicate()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();

        var created = await bed.Context.SendAsync(new CreateProductDocumentTypeCommand { Code = " brochure ", Name = " Folleto ", SortOrder = 5 });

        created.IsSuccess.Should().BeTrue(created.Message);
        created.Data!.Code.Should().Be(ProductDocumentType.NormalizeCode(" brochure "));
        created.Data.Name.Should().Be("Folleto");
        (await bed.Context.SendAsync(new CreateProductDocumentTypeCommand { Code = "tech_sheet", Name = "Otra ficha" }))
            .Message.Should().Be(CatalogErrorCodes.ProductDocumentTypeAlreadyExists);
    }

    [Fact]
    public async Task Update_ChangesTheType_ButNotIntoAnotherCode()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();

        var updated = await bed.Context.SendAsync(new UpdateProductDocumentTypeCommand { Id = bed.TechSheetTypeId, Code = "TECH_SHEET", Name = "Ficha", SortOrder = 2, IsActive = false });

        updated.IsSuccess.Should().BeTrue(updated.Message);
        updated.Data!.IsActive.Should().BeFalse();
        (await bed.Context.SendAsync(new UpdateProductDocumentTypeCommand { Id = bed.TechSheetTypeId, Code = "OTHER", Name = "x" }))
            .Message.Should().Be(CatalogErrorCodes.ProductDocumentTypeAlreadyExists);
        (await bed.Context.SendAsync(new UpdateProductDocumentTypeCommand { Id = Guid.NewGuid(), Code = "X", Name = "x" }))
            .Message.Should().Be(CatalogErrorCodes.ProductDocumentTypeNotFound);
    }

    [Fact]
    public async Task Delete_InUse_IsBlockedWithTheCount_ThenRestoreIsSuperAdminOnly()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        bed.Context.Db.ProductDocuments.Add(new ProductDocument
        {
            CompanyId = bed.CompanyId, ProductId = bed.ProductId, ProductDocumentTypeId = bed.TechSheetTypeId,
            DisplayName = "Ficha", OriginalName = "f.pdf", NewName = "f.pdf", Path = "p", ContentType = "application/pdf"
        });
        await bed.Context.Db.SaveChangesAsync();
        bed.Context.Db.ChangeTracker.Clear();

        var blocked = await bed.Context.SendAsync(new DeleteProductDocumentTypeCommand(bed.TechSheetTypeId));

        blocked.Message.Should().Be(CatalogErrorCodes.ProductDocumentTypeInUse);
        blocked.Errors.Should().ContainSingle(e => e.Contains("Active ProductDocuments: 1"));

        var other = bed.Context.Db.ProductDocumentTypes.Single(t => t.Code == "OTHER").Id;
        (await bed.Context.SendAsync(new DeleteProductDocumentTypeCommand(other))).IsSuccess.Should().BeTrue();
        (await bed.Context.SendAsync(new DeleteProductDocumentTypeCommand(other))).Message.Should().Be(CatalogErrorCodes.ProductDocumentTypeNotFound);

        (await bed.Context.SendAsync(new RestoreProductDocumentTypeCommand(other))).Message.Should().Be(CatalogErrorCodes.SuperAdminRequired);
        bed.Context.IsSuperAdmin = true;
        (await bed.Context.SendAsync(new RestoreProductDocumentTypeCommand(other))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EveryCommand_WithoutCompany_RespondsCompanyRequired()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        bed.Context.ActingCompanyId = Guid.Empty;

        (await bed.Context.SendAsync(new CreateProductDocumentTypeCommand { Code = "A", Name = "A" })).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await bed.Context.SendAsync(new UpdateProductDocumentTypeCommand { Id = bed.TechSheetTypeId, Code = "A", Name = "A" })).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await bed.Context.SendAsync(new DeleteProductDocumentTypeCommand(bed.TechSheetTypeId))).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await bed.Context.SendAsync(new RestoreProductDocumentTypeCommand(bed.TechSheetTypeId))).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
    }

    [Fact]
    public void Validators_CheckCodeNameAndOrder()
    {
        new CreateProductDocumentTypeCommandValidator().Validate(new CreateProductDocumentTypeCommand { Code = "A", Name = "A" }).IsValid.Should().BeTrue();
        new CreateProductDocumentTypeCommandValidator().Validate(new CreateProductDocumentTypeCommand { Code = new string('A', 31), Name = "", SortOrder = -1 })
            .Errors.Should().HaveCount(3);
        new UpdateProductDocumentTypeCommandValidator().Validate(new UpdateProductDocumentTypeCommand { Code = "A", Name = new string('n', 101) })
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Id", "Name"]);
        new DeleteProductDocumentTypeCommandValidator().Validate(new DeleteProductDocumentTypeCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new RestoreProductDocumentTypeCommandValidator().Validate(new RestoreProductDocumentTypeCommand(Guid.NewGuid())).IsValid.Should().BeTrue();
    }
}
