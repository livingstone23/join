using JOIN.Domain.Audit;

namespace JOIN.Domain.Catalog;

/// <summary>
/// A product or service the company offers (table <c>Catalog.Products</c>, SPEC 57). Shared by the customer module
/// (products offered in a lead, SPEC 61) and, later, inventory, purchases, sales and quotes. The code is unique among
/// the live products of the company; <see cref="ReferencePrice"/> is in the company's base currency (SPEC 56).
/// </summary>
public class Product : BaseTenantEntity
{
    /// <summary>Upper-case code without spaces, max 30 characters (<see cref="NormalizeCode"/>).</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ProductKind Kind { get; set; } = ProductKind.Product;

    public Guid? ProductCategoryId { get; set; }

    /// <summary>Reference price in the company's base currency; the real price lives in each offer line.</summary>
    public decimal ReferencePrice { get; set; }

    public bool IsActive { get; set; } = true;

    public virtual ProductCategory? ProductCategory { get; set; }

    /// <summary>Removes every whitespace and upper-cases the code (<c>" crm-usr "</c> → <c>CRM-USR</c>).</summary>
    public static string NormalizeCode(string code) =>
        string.Concat(code.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}
