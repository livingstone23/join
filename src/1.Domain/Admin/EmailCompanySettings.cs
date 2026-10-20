using JOIN.Domain.Audit;

namespace JOIN.Domain.Admin;

/// <summary>
/// SPEC 72 — the sending rules of a company (table <c>Admin.EmailCompanySettings</c>, one live row per
/// company): the sender of the company (empty means the global one of <c>SendGridOptions</c>), the daily
/// quota of emails per user and the total size of the attachments of one email.
/// </summary>
public class EmailCompanySettings : BaseTenantEntity
{
    public const int DefaultMaxEmailsPerUserPerDay = 50;
    public const long DefaultMaxAttachmentsTotalBytes = 15L * 1024 * 1024;
    public const int MaxFromNameLength = 150;
    public const int MaxFromEmailLength = 320;

    /// <summary>Sender of the company. Empty means the global <c>SendGridOptions.FromEmail</c>.</summary>
    public string? FromEmail { get; set; }

    /// <summary>Name shown next to the sender of the company.</summary>
    public string? FromName { get; set; }

    /// <summary>Emails a single user may send per day (the <c>Sent</c> and the <c>Failed</c> of the day count).</summary>
    public int MaxEmailsPerUserPerDay { get; set; } = DefaultMaxEmailsPerUserPerDay;

    /// <summary>Total size of the attachments of one email.</summary>
    public long MaxAttachmentsTotalBytes { get; set; } = DefaultMaxAttachmentsTotalBytes;

    /// <summary>The values a company without a row uses.</summary>
    public static EmailCompanySettings Defaults(Guid companyId) => new() { CompanyId = companyId };
}
