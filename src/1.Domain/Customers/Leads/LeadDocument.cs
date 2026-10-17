using JOIN.Domain.Audit;
using JOIN.Domain.Enums;

namespace JOIN.Domain.Customers.Leads;

/// <summary>
/// A document attached to a lead and, optionally, to one of its contacts (table <c>Customer.LeadDocuments</c>, SPEC 62).
/// Same storage fields as <c>TicketDocument</c>; the file stays while the row exists (logical delete).
/// </summary>
public class LeadDocument : BaseTenantEntity
{
    public Guid LeadId { get; set; }
    public Guid? LeadInteractionId { get; set; }
    public DocumentType DocumentType { get; set; }
    public string OriginalName { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public StorageProviderKind StorageProvider { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public Guid UploadedByUserId { get; set; }
}

/// <summary>
/// Attachment limits of a company for leads (table <c>Customer.LeadAttachmentSettings</c>, one row per company,
/// SPEC 62). Without a row nothing can be attached.
/// </summary>
public class LeadAttachmentSettings : BaseTenantEntity
{
    public const long DefaultMaxFileSizeBytes = 10L * 1024 * 1024;
    public const int DefaultMaxFilesPerInteraction = 5;
    public const int DefaultMaxFilesPerDay = 200;
    public const DocumentType DefaultAllowedTypes = DocumentType.Pdf | DocumentType.Word | DocumentType.Excel | DocumentType.Image;

    public DocumentType AllowedDocumentTypes { get; set; }
    public long MaxFileSizeBytes { get; set; }
    public int MaxFilesPerInteraction { get; set; }
    public int? MaxFilesPerDay { get; set; }
}
