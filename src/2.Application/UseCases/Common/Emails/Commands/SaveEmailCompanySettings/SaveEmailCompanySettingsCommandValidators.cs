using FluentValidation;
using JOIN.Application.Services.Email;
using Settings = JOIN.Domain.Admin.EmailCompanySettings;

namespace JOIN.Application.UseCases.Common.Emails.Commands;

/// <summary>Validates <see cref="CreateEmailCompanySettingsCommand"/>.</summary>
public sealed class CreateEmailCompanySettingsCommandValidator : AbstractValidator<CreateEmailCompanySettingsCommand>
{
    public CreateEmailCompanySettingsCommandValidator() => EmailCompanySettingsValidation.Apply(this);
}

/// <summary>Validates <see cref="UpdateEmailCompanySettingsCommand"/>.</summary>
public sealed class UpdateEmailCompanySettingsCommandValidator : AbstractValidator<UpdateEmailCompanySettingsCommand>
{
    public UpdateEmailCompanySettingsCommandValidator() => EmailCompanySettingsValidation.Apply(this);
}

/// <summary>
/// Rules shared by both validators: an optional sender with a valid format, a positive daily quota and a total size
/// of attachments the provider accepts (SendGrid allows up to 30 MB per message).
/// </summary>
internal static class EmailCompanySettingsValidation
{
    internal const int MaxDailyQuota = 1000;
    internal const long MaxAttachmentsBytes = 30L * 1024 * 1024;

    internal static void Apply<T>(AbstractValidator<T> validator) where T : IEmailCompanySettingsInput
    {
        validator.RuleFor(x => x.FromEmail)
            .MaximumLength(Settings.MaxFromEmailLength)
            .Must(EmailAddressValidator.IsValid).WithMessage("FromEmail must be a valid email address.")
            .When(x => !string.IsNullOrWhiteSpace(x.FromEmail));
        validator.RuleFor(x => x.FromName).MaximumLength(Settings.MaxFromNameLength);
        validator.RuleFor(x => x.MaxEmailsPerUserPerDay).InclusiveBetween(1, MaxDailyQuota);
        validator.RuleFor(x => x.MaxAttachmentsTotalBytes).InclusiveBetween(1, MaxAttachmentsBytes);
    }
}
