using JOIN.Domain.Audit;

namespace JOIN.Domain.Finance;

/// <summary>
/// Global ISO 4217 currency (table <c>Finance.Currencies</c>, SPEC 56). Maintained by the SuperAdmin; each company
/// only activates the ones it uses through <see cref="CompanyCurrency"/>.
/// </summary>
public class Currency : BaseAuditableEntity
{
    /// <summary>ISO 4217 code, three upper-case letters (<c>DOP</c>, <c>USD</c>).</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    /// <summary>Minor-unit digits shown for the currency (0–4).</summary>
    public int Decimals { get; set; } = 2;

    public bool IsActive { get; set; } = true;

    /// <summary>Normalizes an ISO code: trimmed and upper-case.</summary>
    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}
