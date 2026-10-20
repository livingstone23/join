using JOIN.Domain.Audit;

namespace JOIN.Domain.Catalog;

/// <summary>
/// SPEC 70 — catalog of document classifications per company (table <c>Catalog.ProductDocumentTypes</c>).
/// Seed: Ficha técnica (<c>TECH_SHEET</c>), Propuesta (<c>PROPOSAL</c>), Manual (<c>MANUAL</c>), Otro
/// (<c>OTHER</c>). Code is upper-case without spaces and unique per company among live rows.
/// </summary>
public class ProductDocumentType : BaseTenantEntity
{
    public const int MaxCodeLength = 30;
    public const int MaxNameLength = 100;

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary> Removes every whitespace and upper-cases the code (<c>" tech sheet "</c> → <c>TECH_SHEET</c>). </summary>
    public static string NormalizeCode(string code) =>
        string.Concat(code.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}