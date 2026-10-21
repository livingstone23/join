namespace JOIN.Application.DTO.Common.Emails;

/// <summary>
/// SPEC 72 — the sending rules of the company (<c>GET /EmailCompanySettings</c>): the sender of the
/// company (empty means the global one), the daily quota of emails per user and the total size of the
/// attachments of one email.
/// </summary>
public sealed record EmailCompanySettingsDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string? FromEmail { get; init; }
    public string? FromName { get; init; }
    public int MaxEmailsPerUserPerDay { get; init; }
    public long MaxAttachmentsTotalBytes { get; init; }
}
