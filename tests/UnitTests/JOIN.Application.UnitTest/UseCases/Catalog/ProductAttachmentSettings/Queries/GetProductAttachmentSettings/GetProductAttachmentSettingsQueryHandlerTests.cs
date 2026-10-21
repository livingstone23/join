using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Catalog.ProductAttachmentSettings.Queries;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Catalog.ProductAttachmentSettings.Queries.GetProductAttachmentSettings;

/// <summary>SPEC 70 — the company's limits with Dapper, or <c>PRODUCT_ATTACHMENT_SETTINGS_NOT_CONFIGURED</c>.</summary>
public sealed class GetProductAttachmentSettingsQueryHandlerTests
{
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly FakeSqlConnectionFactory _connection = new();

    public GetProductAttachmentSettingsQueryHandlerTests() => _user.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());

    [Fact]
    public async Task Handle_ReturnsTheRow_WithTheFlagNames()
    {
        _connection.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(), ["CompanyId"] = Guid.NewGuid(),
            ["AllowedDocumentTypes"] = (int)(DocumentType.Pdf | DocumentType.Excel), ["MaxDocumentSizeBytes"] = 10L, ["MaxDocumentsPerProduct"] = 20,
            ["AllowedImageFormats"] = (int)ProductImageFormat.Webp, ["MaxImageSizeBytes"] = 5L, ["MaxImagesPerProduct"] = 5,
            ["CreatedAt"] = DateTime.UtcNow
        }));

        var response = await new GetProductAttachmentSettingsQueryHandler(_connection, _user.Object).Handle(new GetProductAttachmentSettingsQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.AllowedDocumentTypeNames.Should().BeEquivalentTo(["Pdf", "Excel"]);
        response.Data.AllowedImageFormatNames.Should().BeEquivalentTo(["Webp"]);
    }

    [Fact]
    public async Task Handle_MissingRowOrCompany()
    {
        var handler = new GetProductAttachmentSettingsQueryHandler(_connection, _user.Object);
        _connection.SetResults(FakeResultSet.Empty("Id"));

        (await handler.Handle(new GetProductAttachmentSettingsQuery(), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.ProductAttachmentSettingsNotConfigured);

        _user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        (await handler.Handle(new GetProductAttachmentSettingsQuery(), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
    }
}
