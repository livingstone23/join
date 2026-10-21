using JOIN.Application.Interface.Email;
using Microsoft.Extensions.Options;

namespace JOIN.Infrastructure.Messaging.SendGrid;

/// <summary>
/// SPEC 72 — the global sender of the installation (<c>SendGrid:FromEmail</c> / <c>SendGrid:FromName</c>), used when the
/// company has no sender of its own (rule 8). Empty values are reported as <c>null</c>.
/// </summary>
public sealed class SendGridSenderDefaults(IOptions<SendGridOptions> options) : IEmailSenderDefaults
{
    public string? FromEmail => string.IsNullOrWhiteSpace(options.Value.FromEmail) ? null : options.Value.FromEmail.Trim();

    public string? FromName => string.IsNullOrWhiteSpace(options.Value.FromName) ? null : options.Value.FromName.Trim();
}
