using JOIN.Application.Common;
using JOIN.Application.DTO.Common.Emails;

namespace JOIN.Application.UseCases.Common.Emails.Commands;

/// <summary>Fields shared by the create and update commands of the sending rules of the company (SPEC 72).</summary>
public interface IEmailCompanySettingsInput
{
    string? FromEmail { get; }
    string? FromName { get; }
    int MaxEmailsPerUserPerDay { get; }
    long MaxAttachmentsTotalBytes { get; }
}

/// <summary><c>POST /EmailCompanySettings</c> (SPEC 72): creates the company's row; 409 when it already exists.</summary>
public sealed record CreateEmailCompanySettingsCommand : IEmailCompanySettingsInput, ITransactionalCommand<Response<EmailCompanySettingsDto>>
{
    public string? FromEmail { get; init; }
    public string? FromName { get; init; }
    public int MaxEmailsPerUserPerDay { get; init; }
    public long MaxAttachmentsTotalBytes { get; init; }
}

/// <summary><c>PUT /EmailCompanySettings</c> (SPEC 72): updates the company's row.</summary>
public sealed record UpdateEmailCompanySettingsCommand : IEmailCompanySettingsInput, ITransactionalCommand<Response<EmailCompanySettingsDto>>
{
    public string? FromEmail { get; init; }
    public string? FromName { get; init; }
    public int MaxEmailsPerUserPerDay { get; init; }
    public long MaxAttachmentsTotalBytes { get; init; }
}
