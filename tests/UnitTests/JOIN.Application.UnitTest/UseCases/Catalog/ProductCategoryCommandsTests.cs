using FluentAssertions;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UnitTest.UseCases.Finance;
using JOIN.Application.UseCases.Catalog.ProductCategories.Commands;
using JOIN.Application.UseCases.Catalog.Products.Commands;

namespace JOIN.Application.UnitTest.UseCases.Catalog;

/// <summary>
/// SPEC 57: categories nest without cycles, names are unique among siblings, in-use categories are not deleted.
/// </summary>
public sealed class ProductCategoryCommandsTests
{
    [Fact]
    public async Task Create_ShouldNestAndRejectDuplicateSiblingNames()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        var root = (await ctx.SendAsync(new CreateProductCategoryCommand { Name = " Software " })).Data!;

        var child = await ctx.SendAsync(new CreateProductCategoryCommand { Name = "Licencias", ParentId = root.Id });

        root.Name.Should().Be("Software");
        child.IsSuccess.Should().BeTrue(child.Message);
        child.Data!.ParentName.Should().Be("Software");
        (await ctx.SendAsync(new CreateProductCategoryCommand { Name = "software" })).Message.Should().Be(CatalogErrorCodes.DuplicateKey);
        (await ctx.SendAsync(new CreateProductCategoryCommand { Name = "Licencias" })).IsSuccess.Should().BeTrue("another parent");
        (await ctx.SendAsync(new CreateProductCategoryCommand { Name = "X", ParentId = Guid.NewGuid() })).Message.Should().Be(CatalogErrorCodes.ProductCategoryNotFound);
    }

    [Fact]
    public async Task Update_CannotMakeACategoryItsOwnAncestor()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        var a = (await ctx.SendAsync(new CreateProductCategoryCommand { Name = "A" })).Data!;
        var b = (await ctx.SendAsync(new CreateProductCategoryCommand { Name = "B", ParentId = a.Id })).Data!;
        var c = (await ctx.SendAsync(new CreateProductCategoryCommand { Name = "C", ParentId = b.Id })).Data!;

        (await ctx.SendAsync(new UpdateProductCategoryCommand { Id = a.Id, Name = "A", ParentId = a.Id })).Message.Should().Be(CatalogErrorCodes.ProductCategoryCycle);
        (await ctx.SendAsync(new UpdateProductCategoryCommand { Id = a.Id, Name = "A", ParentId = c.Id })).Message.Should().Be(CatalogErrorCodes.ProductCategoryCycle);

        var moved = await ctx.SendAsync(new UpdateProductCategoryCommand { Id = c.Id, Name = "C2", ParentId = a.Id, SortOrder = 3, IsActive = false });
        moved.IsSuccess.Should().BeTrue(moved.Message);
        moved.Data!.Name.Should().Be("C2");
        (await ctx.SendAsync(new UpdateProductCategoryCommand { Id = Guid.NewGuid(), Name = "Z" })).Message.Should().Be(CatalogErrorCodes.ProductCategoryNotFound);
    }

    [Fact]
    public async Task Delete_InUse_ShouldFailWithDetail_AndRestoreNeedsALiveParent()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        var parent = (await ctx.SendAsync(new CreateProductCategoryCommand { Name = "Parent" })).Data!;
        var child = (await ctx.SendAsync(new CreateProductCategoryCommand { Name = "Child", ParentId = parent.Id })).Data!;
        await ctx.SendAsync(new CreateProductCommand { Code = "P1", Name = "P1", Kind = 1, ProductCategoryId = parent.Id });

        var inUse = await ctx.SendAsync(new DeleteProductCategoryCommand(parent.Id));
        inUse.Message.Should().Be(CatalogErrorCodes.ProductCategoryInUse);
        inUse.Errors.Should().Equal("Active Products: 1", "Active ProductCategories: 1");

        (await ctx.SendAsync(new DeleteProductCategoryCommand(child.Id))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new DeleteProductCategoryCommand(child.Id))).Message.Should().Be(CatalogErrorCodes.ProductCategoryNotFound);

        ctx.IsSuperAdmin = true;
        (await ctx.SendAsync(new RestoreProductCategoryCommand(child.Id))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_WithADeletedParent_ShouldFail()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        var parent = (await ctx.SendAsync(new CreateProductCategoryCommand { Name = "Parent" })).Data!;
        var child = (await ctx.SendAsync(new CreateProductCategoryCommand { Name = "Child", ParentId = parent.Id })).Data!;
        await ctx.SendAsync(new DeleteProductCategoryCommand(child.Id));
        await ctx.SendAsync(new DeleteProductCategoryCommand(parent.Id));

        (await ctx.SendAsync(new RestoreProductCategoryCommand(child.Id))).Message.Should().Be(CatalogErrorCodes.SuperAdminRequired);
        ctx.IsSuperAdmin = true;
        (await ctx.SendAsync(new RestoreProductCategoryCommand(child.Id))).Message.Should().Be("PARENT_DELETED");
    }

    [Fact]
    public async Task Commands_WithoutCompany_ShouldReturnCompanyRequired()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        ctx.ActingCompanyId = Guid.Empty;

        (await ctx.SendAsync(new CreateProductCategoryCommand { Name = "A" })).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateProductCategoryCommand { Id = Guid.NewGuid(), Name = "A" })).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteProductCategoryCommand(Guid.NewGuid()))).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new RestoreProductCategoryCommand(Guid.NewGuid()))).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
    }
}
