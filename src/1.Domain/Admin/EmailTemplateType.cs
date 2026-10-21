using JOIN.Domain.Audit;

namespace JOIN.Domain.Admin;

/// <summary>
/// SPEC 71 — catalog of email template classifications per company (table <c>Admin.EmailTemplateTypes</c>).
/// Seed: Presentación (<c>INTRO</c>), Propuesta (<c>PROPOSAL</c>), Seguimiento (<c>FOLLOW_UP</c>),
/// Agradecimiento (<c>THANKS</c>). The code is upper-case without spaces and unique per company
/// among live rows.
/// </summary>
public class EmailTemplateType : BaseTenantEntity
{
    public const int MaxCodeLength = 30;
    public const int MaxNameLength = 100;

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary> Removes every whitespace and upper-cases the code (<c>" follow up "</c> → <c>FOLLOW_UP</c>). </summary>
    public static string NormalizeCode(string code) =>
        string.Concat(code.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}
