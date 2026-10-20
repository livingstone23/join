namespace JOIN.Application.Interface.Email;

/// <summary>
/// SPEC 72 — a file attached to an email. The content travels in memory: the attachment is always a
/// document that already exists in the storage of the company (SPEC 70 / SPEC 62), never a loose file.
/// </summary>
/// <param name="FileName">Name shown to the recipient.</param>
/// <param name="ContentType">MIME type of the file.</param>
/// <param name="Content">Bytes of the file.</param>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>
/// SPEC 72 — the message handed to the provider. The sender is optional: when it is empty the adapter
/// uses the global one of its own configuration.
/// </summary>
public sealed record EmailMessage
{
    /// <summary>Recipient of the message.</summary>
    public string To { get; init; } = string.Empty;

    /// <summary>Copies of the message (SPEC 72, rule 6: up to 5).</summary>
    public IReadOnlyList<string> Cc { get; init; } = [];

    /// <summary>Sender of the company; empty means the global sender of the provider.</summary>
    public string? FromEmail { get; init; }

    /// <summary>Name shown next to the sender of the company.</summary>
    public string? FromName { get; init; }

    /// <summary>Reply-To: the mailbox of the user who sends the message (SPEC 72, rule 8).</summary>
    public string? ReplyTo { get; init; }

    public string Subject { get; init; } = string.Empty;

    public string HtmlBody { get; init; } = string.Empty;

    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}

/// <summary>
/// SPEC 72 — the outcome of a delivery. <see cref="ProviderMessageId"/> is the identifier the provider
/// gives to the message (the <c>X-Message-Id</c> of SendGrid), kept in the history of the email.
/// </summary>
/// <param name="Success">Whether the provider accepted the message.</param>
/// <param name="ProviderMessageId">Identifier of the message in the provider, when it was accepted.</param>
/// <param name="Error">Reason of the rejection, when it was not.</param>
public sealed record EmailSendResult(bool Success, string? ProviderMessageId, string? Error)
{
    /// <summary>The provider accepted the message.</summary>
    public static EmailSendResult Ok(string? providerMessageId) => new(true, providerMessageId, null);

    /// <summary>The provider rejected the message.</summary>
    public static EmailSendResult Fail(string error) => new(false, null, error);
}
