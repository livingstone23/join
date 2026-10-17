namespace JOIN.Domain.Customers.Leads;

/// <summary>
/// Fields every per-company commercial catalog row shares (SPEC 59): code unique per company, name, order and state.
/// </summary>
public interface ICommercialCatalogItem
{
    Guid Id { get; }
    Guid CompanyId { get; set; }
    string Code { get; set; }
    string Name { get; set; }
    int SortOrder { get; set; }
    bool IsActive { get; set; }
    int GcRecord { get; }
}

/// <summary>Code normalization shared by the commercial catalogs (same rule as <c>CustomerStage.NormalizeCode</c>).</summary>
public static class CommercialCode
{
    /// <summary>Removes whitespace and upper-cases the code.</summary>
    public static string Normalize(string code) =>
        string.Concat(code.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}
