using JOIN.Domain.Audit;

namespace JOIN.Domain.Admin;

/// <summary>
/// SPEC 72 — where an email was sent from. <c>Person</c>: the detail of the person; <c>Customer</c>: the
/// detail of the customer (it delegates on the person of the customer); <c>Lead</c>: the detail of the
/// lead (it delegates on the person of the lead).
/// </summary>
public enum PersonEmailOrigin
{
    Person = 1,
    Customer = 2,
    Lead = 3
}

/// <summary>SPEC 72 — outcome of the delivery: <c>Sent</c> or <c>Failed</c>.</summary>
public enum PersonEmailStatus
{
    Sent = 1,
    Failed = 2
}

/// <summary>
/// SPEC 72 — origin of an attachment: a live document of a product of the company (SPEC 70) or a live
/// document of the associated lead (SPEC 62). Loose files are not attached.
/// </summary>
public enum PersonEmailAttachmentSourceKind
{
    ProductDocument = 1,
    LeadDocument = 2
}

/// <summary>
/// SPEC 72 — an email sent to a person (table <c>Admin.PersonEmailMessages</c>). Every send leaves a row,
/// whatever its origin (person, customer or lead) and whatever its result (<c>Sent</c> or <c>Failed</c>).
/// The body is the sanitized HTML that was sent; the plain-text excerpt of the lead interaction is
/// derived from it.
/// </summary>
public class PersonEmailMessage : BaseTenantEntity
{
    public const int MaxToAddressLength = 320;
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 50_000;
    public const int MaxProviderMessageIdLength = 200;
    public const int MaxErrorMessageLength = 500;

    public Guid PersonId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? LeadId { get; set; }

    /// <summary>Contact of the lead created by this email (SPEC 72, rule 4).</summary>
    public Guid? LeadInteractionId { get; set; }

    public Guid? EmailTemplateId { get; set; }

    public PersonEmailOrigin Origin { get; set; }

    public string ToAddress { get; set; } = string.Empty;

    /// <summary>CC addresses, separated by comma (SPEC 72, rule 6).</summary>
    public string? CcAddresses { get; set; }

    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Reply-To: the mailbox of the user who sent the email (SPEC 72, rule 8).</summary>
    public string? ReplyToAddress { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string BodyHtml { get; set; } = string.Empty;

    public PersonEmailStatus Status { get; set; } = PersonEmailStatus.Sent;

    /// <summary>Identifier of the message in the provider (the <c>X-Message-Id</c> of SendGrid).</summary>
    public string? ProviderMessageId { get; set; }

    public string? ErrorMessage { get; set; }

    public Guid SentByUserId { get; set; }

    public DateTime SentAt { get; set; }
}

/// <summary>
/// SPEC 72 — a document attached to an email (table <c>Admin.PersonEmailMessageAttachments</c>). The file
/// itself is not copied: the row points at the document it came from, so the history stays light.
/// </summary>
public class PersonEmailMessageAttachment : BaseTenantEntity
{
    public Guid PersonEmailMessageId { get; set; }

    public PersonEmailAttachmentSourceKind SourceKind { get; set; }

    /// <summary>Identifier of the source document (product document or lead document).</summary>
    public Guid SourceId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
}
