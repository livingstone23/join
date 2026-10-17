using FluentAssertions;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UnitTest.UseCases.Finance;
using JOIN.Application.UseCases.Catalog.ProductCategories.Commands;
using JOIN.Application.UseCases.Catalog.Products.Commands;
using JOIN.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Application.UnitTest.UseCases.Catalog;

/// <summary>
/// SPEC 57: per-company catalog, normalized unique code, price in the base currency.
/// </summary>
public sealed class ProductCommandsTests
{
    private static CreateProductCommand NewProduct(string code, Guid? categoryId = null, decimal price = 100m) => new()
    {
        Code = code,
        Name = $" Producto {code} ",
        Description = "  ",
        Kind = (int)ProductKind.Service,
        ProductCategoryId = categoryId,
        ReferencePrice = price
    };

    [Fact]
    public async Task Create_ShouldNormalizeTheCode_AndCarryTheBaseCurrency()
    {
        using var ctx = await FinanceTestContext.CreateAsync();

        var response = await ctx.SendAsync(NewProduct(" crm-usr "));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Code.Should().Be("CRM-USR");
        response.Data.Name.Should().Be("Producto  crm-usr");
        response.Data.Description.Should().BeNull();
        response.Data.Kind.Should().Be((int)ProductKind.Service);
        response.Data.BaseCurrencyCode.Should().Be("DOP");
    }

    [Fact]
    public async Task Create_WithADuplicateCodeInTheCompany_ShouldFail_ButAnotherCompanyCanUseIt()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        await ctx.SendAsync(NewProduct("CRM-USR"));

        (await ctx.SendAsync(NewProduct("crm-usr"))).Message.Should().Be(CatalogErrorCodes.DuplicateKey);

        var other = new global::JOIN.Domain.Common.Company { Name = "Other", TaxId = "OTH-1", IsActive = true };
        ctx.Db.Companies.Add(other);
        ctx.Db.CompanyCurrencies.Add(new global::JOIN.Domain.Finance.CompanyCurrency { CompanyId = other.Id, CurrencyId = ctx.Currency["DOP"], IsBase = true });
        await ctx.Db.SaveChangesAsync();
        ctx.ActingCompanyId = other.Id;

        (await ctx.SendAsync(NewProduct("CRM-USR"))).IsSuccess.Should().BeTrue("the code is unique per company");
    }

    [Fact]
    public async Task Create_WithoutBaseCurrencyOrWithAForeignCategory_ShouldFail()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        (await ctx.SendAsync(NewProduct("A", categoryId: Guid.NewGuid()))).Message.Should().Be(CatalogErrorCodes.ProductCategoryNotFound);

        await ctx.RemoveBaseCurrencyAsync();
        (await ctx.SendAsync(NewProduct("B"))).Message.Should().Be(CatalogErrorCodes.BaseCurrencyRequired);
    }

    [Fact]
    public async Task UpdateDeleteAndRestore_ShouldFollowTheRules()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        var category = (await ctx.SendAsync(new CreateProductCategoryCommand { Name = "Software" })).Data!;
        var first = (await ctx.SendAsync(NewProduct("A"))).Data!;
        var second = (await ctx.SendAsync(NewProduct("B"))).Data!;

        var updated = await ctx.SendAsync(new UpdateProductCommand
        {
            Id = first.Id, Code = "a-1", Name = "Licencia", Kind = (int)ProductKind.Product, ProductCategoryId = category.Id, ReferencePrice = 1450m, IsActive = false
        });
        updated.IsSuccess.Should().BeTrue(updated.Message);
        updated.Data!.Code.Should().Be("A-1");
        updated.Data.CategoryName.Should().Be("Software");
        updated.Data.IsActive.Should().BeFalse();

        (await ctx.SendAsync(new UpdateProductCommand { Id = second.Id, Code = "A-1", Name = "x", Kind = 1 })).Message.Should().Be(CatalogErrorCodes.DuplicateKey);
        (await ctx.SendAsync(new UpdateProductCommand { Id = Guid.NewGuid(), Code = "Z", Name = "x", Kind = 1 })).Message.Should().Be(CatalogErrorCodes.ProductNotFound);

        (await ctx.SendAsync(new DeleteProductCommand(second.Id))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new DeleteProductCommand(second.Id))).Message.Should().Be(CatalogErrorCodes.ProductNotFound);

        (await ctx.SendAsync(new RestoreProductCommand(second.Id))).Message.Should().Be(CatalogErrorCodes.SuperAdminRequired);
        await ctx.SendAsync(NewProduct("B"));
        ctx.IsSuperAdmin = true;
        (await ctx.SendAsync(new RestoreProductCommand(second.Id))).Message.Should().Be("ACTIVE_DUPLICATE_EXISTS");
    }

    [Fact]
    public async Task Commands_WithoutCompany_ShouldReturnCompanyRequired()
    {
        using var ctx = await FinanceTestContext.CreateAsync();
        ctx.ActingCompanyId = Guid.Empty;

        (await ctx.SendAsync(NewProduct("A"))).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateProductCommand { Id = Guid.NewGuid(), Code = "A", Name = "x", Kind = 1 })).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteProductCommand(Guid.NewGuid()))).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new RestoreProductCommand(Guid.NewGuid()))).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
    }
}
