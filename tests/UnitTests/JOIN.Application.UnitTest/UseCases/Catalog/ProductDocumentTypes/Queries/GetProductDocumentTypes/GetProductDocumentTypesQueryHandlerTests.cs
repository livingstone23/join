using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Catalog.ProductDocumentTypes.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Catalog.ProductDocumentTypes.Queries.GetProductDocumentTypes;

/// <summary>SPEC 70 — the company's document types with Dapper; inactive rows only on request.</summary>
public sealed class GetProductDocumentTypesQueryHandlerTests
{
    [Fact]
    public async Task Handle_FiltersByTenant_AndPassesIncludeInactive()
    {
        var companyId = Guid.NewGuid();
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.CompanyId).Returns(companyId);
        var connection = new FakeSqlConnectionFactory();
        connection.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(), ["CompanyId"] = companyId, ["Code"] = "TECH_SHEET", ["Name"] = "Ficha técnica",
            ["SortOrder"] = 1, ["IsActive"] = true, ["CreatedAt"] = DateTime.UtcNow
        }));

        var response = await new GetProductDocumentTypesQueryHandler(connection, user.Object).Handle(new GetProductDocumentTypesQuery(true), CancellationToken.None);

        response.Data!.Should().ContainSingle(t => t.Code == "TECH_SHEET");
        connection.CapturedParameters["IncludeInactive"].Should().Be(true);
        connection.CapturedParameters["CompanyId"].Should().Be(companyId);
        connection.LastCommandText.Should().Contain("GcRecord = 0");
    }

    [Fact]
    public async Task Handle_WithoutCompany_RespondsCompanyRequired()
    {
        var user = new Mock<ICurrentUserService>();

        (await new GetProductDocumentTypesQueryHandler(new FakeSqlConnectionFactory(), user.Object).Handle(new GetProductDocumentTypesQuery(), CancellationToken.None))
            .Message.Should().Be(CatalogErrorCodes.CompanyRequired);
    }
}
