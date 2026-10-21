using FluentAssertions;
using JOIN.Application.UseCases.Catalog.Products.Images.Commands;

namespace JOIN.Application.UnitTest.UseCases.Catalog.Products.Images.Commands.ManageProductImages;

/// <summary>SPEC 70 — shape checks of the image commands (the company limits are the handler's job).</summary>
public sealed class ManageProductImagesCommandValidatorTests
{
    [Fact]
    public void Upload_RequiresProductNameTypeAndContent()
    {
        var validator = new UploadProductImageCommandValidator();

        validator.Validate(new UploadProductImageCommand(Guid.NewGuid(), "foto.png", "image/png", [1])).IsValid.Should().BeTrue();
        validator.Validate(new UploadProductImageCommand(Guid.Empty, "", "", [])).Errors.Select(e => e.PropertyName)
            .Should().BeEquivalentTo(["ProductId", "OriginalName", "ContentType", "Content"]);
        validator.Validate(new UploadProductImageCommand(Guid.NewGuid(), new string('a', 151), "image/png", [1])).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Reorder_RejectsDuplicatesAndEmptyIds()
    {
        var validator = new ReorderProductImagesCommandValidator();
        var id = Guid.NewGuid();

        validator.Validate(new ReorderProductImagesCommand { ProductId = Guid.NewGuid(), ImageIds = [id, Guid.NewGuid()] }).IsValid.Should().BeTrue();
        validator.Validate(new ReorderProductImagesCommand { ProductId = Guid.NewGuid(), ImageIds = [id, id] }).IsValid.Should().BeFalse();
        validator.Validate(new ReorderProductImagesCommand { ProductId = Guid.NewGuid(), ImageIds = [Guid.Empty] }).IsValid.Should().BeFalse();
        validator.Validate(new ReorderProductImagesCommand { ImageIds = [] }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void SetPrimaryDeleteAndRestore_RequireBothIds()
    {
        new SetPrimaryProductImageCommandValidator().Validate(new SetPrimaryProductImageCommand(Guid.Empty, Guid.Empty)).Errors.Should().HaveCount(2);
        new DeleteProductImageCommandValidator().Validate(new DeleteProductImageCommand(Guid.Empty, Guid.Empty)).Errors.Should().HaveCount(2);
        new RestoreProductImageCommandValidator().Validate(new RestoreProductImageCommand(Guid.Empty, Guid.Empty)).Errors.Should().HaveCount(2);
        new SetPrimaryProductImageCommandValidator().Validate(new SetPrimaryProductImageCommand(Guid.NewGuid(), Guid.NewGuid())).IsValid.Should().BeTrue();
    }
}
