using JOIN.Domain.Audit;

namespace JOIN.Domain.Catalog;

/// <summary>
/// Category of the company's product catalog (table <c>Catalog.ProductCategories</c>, SPEC 57). Categories nest
/// through <see cref="ParentId"/> without cycles; a name is unique among the live siblings of the same parent.
/// </summary>
public class ProductCategory : BaseTenantEntity
{
    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public virtual ProductCategory? Parent { get; set; }
}
