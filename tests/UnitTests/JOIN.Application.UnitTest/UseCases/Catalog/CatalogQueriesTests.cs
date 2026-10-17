using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Catalog.ProductCategories.Commands;
using JOIN.Application.UseCases.Catalog.ProductCategories.Queries;
using JOIN.Application.UseCases.Catalog.Products.Commands;
using JOIN.Application.UseCases.Catalog.Products.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Catalog;

/// <summary>
/// SPEC 57 queries, validators and the reference-price amount source.
/// </summary>
public sealed class CatalogQueriesTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly IOptions<PaginationSettings> _paging = Options.Create(new PaginationSettings());

    public CatalogQueriesTests()
    {
        _user.SetupGet(x => x.CompanyId).Returns(_companyId);
    }

    [Fact]
    public async Task GetProducts_ShouldFilterByTenantSearchKindCategoryAndActive()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id", "Code"), FakeResultSet.FromScalar(0));
        var categoryId = Guid.NewGuid();

        var response = await new GetProductsQueryHandler(factory, _paging, _user.Object)
            .Handle(new GetProductsQuery("crm", 2, categoryId, true, CompanyId: Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        factory.CapturedParameters["CompanyId"].Should().Be(_companyId);
        factory.CapturedParameters["Search"].Should().Be("%crm%");
        factory.LastCommandText.Should().Contain("p.Code ILIKE @Search").And.Contain("p.Kind = @Kind")
            .And.Contain("p.ProductCategoryId = @ProductCategoryId").And.Contain("p.IsActive = @IsActive").And.Contain("p.GcRecord = 0");
    }

    [Fact]
    public async Task GetProductById_And_GetCategoryById_ShouldReturnNotFound()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"));

        (await new GetProductByIdQueryHandler(factory, _user.Object).Handle(new GetProductByIdQuery(Guid.NewGuid()), CancellationToken.None))
            .Message.Should().Be(CatalogErrorCodes.ProductNotFound);
        (await new GetProductCategoryByIdQueryHandler(factory, _user.Object).Handle(new GetProductCategoryByIdQuery(Guid.NewGuid()), CancellationToken.None))
            .Message.Should().Be(CatalogErrorCodes.ProductCategoryNotFound);
    }

    [Fact]
    public async Task Lookup_ShouldReturnOnlyActiveProductsOfTheCompany_UpTo20()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["Code"] = "CRM-USR", ["Name"] = "Licencia", ["Kind"] = 2, ["ReferencePrice"] = 1450m, ["BaseCurrencyCode"] = "DOP" }));

        var response = await new GetProductLookupQueryHandler(factory, _user.Object).Handle(new GetProductLookupQuery(" lic "), CancellationToken.None);

        response.Data!.Single().BaseCurrencyCode.Should().Be("DOP");
        factory.CapturedParameters["Limit"].Should().Be(20);
        factory.CapturedParameters["Search"].Should().Be("%lic%");
        factory.LastCommandText.Should().Contain("p.IsActive = TRUE").And.Contain("p.GcRecord = 0");
    }

    [Fact]
    public async Task GetCategories_ShouldScopeAndFilterInactive()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id", "Name"));

        var response = await new GetProductCategoriesQueryHandler(factory, _user.Object).Handle(new GetProductCategoriesQuery(IncludeInactive: false), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        factory.LastCommandText.Should().Contain("c.IsActive = TRUE").And.Contain("ProductsCount").And.Contain("ChildrenCount");
    }

    [Fact]
    public async Task AmountSource_ShouldAskForLivePricedProductsOfTheCompany()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.FromScalar(true));
        var source = new ProductReferencePriceAmountSource(factory);

        (await source.HasAmountsAsync(_companyId)).Should().BeTrue();
        source.Name.Should().Be("Products");
        factory.CapturedParameters["CompanyId"].Should().Be(_companyId);
        factory.LastCommandText.Should().Contain("p.ReferencePrice > 0").And.Contain("p.GcRecord = 0");
    }

    [Fact]
    public async Task Queries_WithoutCompany_ShouldReturnCompanyRequired()
    {
        _user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        var factory = new FakeSqlConnectionFactory();

        (await new GetProductsQueryHandler(factory, _paging, _user.Object).Handle(new GetProductsQuery(), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await new GetProductByIdQueryHandler(factory, _user.Object).Handle(new GetProductByIdQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await new GetProductLookupQueryHandler(factory, _user.Object).Handle(new GetProductLookupQuery(), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await new GetProductCategoriesQueryHandler(factory, _user.Object).Handle(new GetProductCategoriesQuery(), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await new GetProductCategoryByIdQueryHandler(factory, _user.Object).Handle(new GetProductCategoryByIdQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
    }

    [Theory]
    [InlineData("CRM-USR", 1, "10.50", true)]
    [InlineData("  ", 1, "0", false)]
    [InlineData("A23456789012345678901234567890X", 1, "0", false)]
    [InlineData("OK", 3, "0", false)]
    [InlineData("OK", 2, "-1", false)]
    [InlineData("OK", 2, "1.234", false)]
    public void ProductValidators_ShouldCheckCodeKindAndPrice(string code, int kind, string price, bool valid)
    {
        new CreateProductCommandValidator().Validate(new CreateProductCommand { Code = code, Name = "Name", Kind = kind, ReferencePrice = decimal.Parse(price) })
            .IsValid.Should().Be(valid);
        new UpdateProductCommandValidator().Validate(new UpdateProductCommand { Id = Guid.NewGuid(), Code = code, Name = "Name", Kind = kind, ReferencePrice = decimal.Parse(price) })
            .IsValid.Should().Be(valid);
    }

    [Fact]
    public void CategoryValidators_ShouldRequireNameAndAValidParent()
    {
        new CreateProductCategoryCommandValidator().Validate(new CreateProductCategoryCommand { Name = "" }).IsValid.Should().BeFalse();
        new CreateProductCategoryCommandValidator().Validate(new CreateProductCategoryCommand { Name = "A", ParentId = Guid.Empty }).IsValid.Should().BeFalse();
        new UpdateProductCategoryCommandValidator().Validate(new UpdateProductCategoryCommand { Name = "A" }).IsValid.Should().BeFalse();
        new UpdateProductCategoryCommandValidator().Validate(new UpdateProductCategoryCommand { Id = Guid.NewGuid(), Name = "A" }).IsValid.Should().BeTrue();
    }
}
