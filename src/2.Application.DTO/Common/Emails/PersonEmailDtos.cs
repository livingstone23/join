namespace JOIN.Application.DTO.Common.Emails;

/// <summary>
/// SPEC 72 — a valid email address of a person (<c>GET /Persons/{id}/email-addresses</c>). Only the
/// live and active contacts of type <c>PrimaryEmail</c> or <c>AlternativeEmail</c> whose value passes
/// the format validation are listed.
/// </summary>
public sealed record PersonEmailAddressDto
{
    public Guid Id { get; init; }
    public string Value { get; init; } = string.Empty;
    public bool IsPrimary { get; init; }
    /// <summary><c>PrimaryEmail</c> or <c>AlternativeEmail</c>.</summary>
    public string ContactType { get; init; } = string.Empty;
}

/// <summary>
/// SPEC 72 — a document that can be attached to an email
/// (<c>GET /Persons/{id}/emails/attachment-candidates</c>).
/// </summary>
public sealed record PersonEmailAttachmentCandidateDto
{
    /// <summary><c>ProductDocument</c> or <c>LeadDocument</c>.</summary>
    public string SourceKind { get; init; } = string.Empty;
    public Guid SourceId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    /// <summary>Product or lead the document comes from, for the picker.</summary>
    public string? OriginName { get; init; }
}

/// <summary>An attachment of a sent email (SPEC 72).</summary>
public sealed record PersonEmailAttachmentDto
{
    public Guid Id { get; init; }
    /// <summary><c>ProductDocument</c> or <c>LeadDocument</c>.</summary>
    public string SourceKind { get; init; } = string.Empty;
    public Guid SourceId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
}

/// <summary>
/// SPEC 72 — one row of the email history of a person, a customer or a lead
/// (<c>GET /Persons/{id}/emails</c>). <c>Origin</c>: 1 Person, 2 Customer, 3 Lead.
/// <c>Status</c>: 1 Sent, 2 Failed.
/// </summary>
public sealed record PersonEmailMessageListItemDto
{
    public Guid Id { get; init; }
    public DateTime SentAt { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string ToAddress { get; init; } = string.Empty;
    /// <summary>1 Sent, 2 Failed.</summary>
    public int Status { get; init; }
    /// <summary>1 Person, 2 Customer, 3 Lead.</summary>
    public int Origin { get; init; }
    public Guid? LeadId { get; init; }
    public string? LeadCode { get; init; }
    public Guid SentByUserId { get; init; }
    public string SentByUserName { get; init; } = string.Empty;
    public int AttachmentCount { get; init; }
}

/// <summary>SPEC 72 — the detail of a sent email: the body, the copies and the attachments.</summary>
public sealed record PersonEmailMessageDto
{
    public Guid Id { get; init; }
    public Guid PersonId { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? LeadId { get; init; }
    public Guid? LeadInteractionId { get; init; }
    public Guid? EmailTemplateId { get; init; }
    /// <summary>1 Person, 2 Customer, 3 Lead.</summary>
    public int Origin { get; init; }
    public string ToAddress { get; init; } = string.Empty;
    public IReadOnlyList<string> CcAddresses { get; init; } = [];
    public string FromAddress { get; init; } = string.Empty;
    public string? ReplyToAddress { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string BodyHtml { get; init; } = string.Empty;
    /// <summary>1 Sent, 2 Failed.</summary>
    public int Status { get; init; }
    public string? ProviderMessageId { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid SentByUserId { get; init; }
    public string SentByUserName { get; init; } = string.Empty;
    public DateTime SentAt { get; init; }
    public IReadOnlyList<PersonEmailAttachmentDto> Attachments { get; init; } = [];
}
