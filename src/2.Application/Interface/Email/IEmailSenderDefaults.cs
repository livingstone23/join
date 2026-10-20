namespace JOIN.Application.Interface.Email;

/// <summary>
/// SPEC 72 — the global sender of the installation (the <c>SendGrid:FromEmail</c> / <c>SendGrid:FromName</c>
/// configuration). The Application layer asks for it when the company has no sender of its own
/// (rule 8); the implementation lives in the Infrastructure layer, which owns the configuration.
/// </summary>
public interface IEmailSenderDefaults
{
    /// <summary>The global sender address, or <c>null</c> when it is not configured.</summary>
    string? FromEmail { get; }

    /// <summary>The name shown next to the global sender, or <c>null</c>.</summary>
    string? FromName { get; }
}
