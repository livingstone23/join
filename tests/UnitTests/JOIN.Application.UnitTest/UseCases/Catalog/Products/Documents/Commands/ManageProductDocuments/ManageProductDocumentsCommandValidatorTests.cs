using FluentAssertions;
using JOIN.Application.UseCases.Catalog.Products.Documents.Commands;

namespace JOIN.Application.UnitTest.UseCases.Catalog.Products.Documents.Commands.ManageProductDocuments;

/// <summary>SPEC 70 — <c>DisplayName</c> up to 200, <c>Description</c> up to 1000, file and type required.</summary>
public sealed class ManageProductDocumentsCommandValidatorTests
{
    private static UploadProductDocumentCommand Upload(string displayName = "Ficha", string? description = null, byte[]? content = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), displayName, description, "ficha.pdf", "application/pdf", content ?? [1]);

    [Fact]
    public void Upload_Limits()
    {
        var validator = new UploadProductDocumentCommandValidator();

        validator.Validate(Upload()).IsValid.Should().BeTrue();
        validator.Validate(Upload(new string('a', 200), new string('b', 1000))).IsValid.Should().BeTrue();
        validator.Validate(Upload(new string('a', 201))).IsValid.Should().BeFalse();
        validator.Validate(Upload(description: new string('b', 1001))).IsValid.Should().BeFalse();
        validator.Validate(Upload(" ")).IsValid.Should().BeFalse();
        validator.Validate(Upload(content: [])).IsValid.Should().BeFalse();
        validator.Validate(new UploadProductDocumentCommand(Guid.Empty, Guid.Empty, "a", null, "", "", [1])).Errors.Select(e => e.PropertyName)
            .Should().BeEquivalentTo(["ProductId", "ProductDocumentTypeId", "OriginalName", "ContentType"]);
    }

    [Fact]
    public void DeleteAndRestore_RequireBothIds()
    {
        new DeleteProductDocumentCommandValidator().Validate(new DeleteProductDocumentCommand(Guid.Empty, Guid.Empty)).Errors.Should().HaveCount(2);
        new RestoreProductDocumentCommandValidator().Validate(new RestoreProductDocumentCommand(Guid.Empty, Guid.Empty)).Errors.Should().HaveCount(2);
        new DeleteProductDocumentCommandValidator().Validate(new DeleteProductDocumentCommand(Guid.NewGuid(), Guid.NewGuid())).IsValid.Should().BeTrue();
    }
}
