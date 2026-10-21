using FluentValidation;
using JOIN.Domain.Admin;

namespace JOIN.Application.UseCases.Common.Emails.Commands;

/// <summary>Validates <see cref="SendPersonEmailCommand"/>.</summary>
public sealed class SendPersonEmailCommandValidator : AbstractValidator<SendPersonEmailCommand>
{
    public SendPersonEmailCommandValidator()
    {
        RuleFor(x => x.PersonId).NotEmpty();
        PersonEmailInputValidation.Apply(this);
    }
}

/// <summary>Validates <see cref="SendCustomerEmailCommand"/>.</summary>
public sealed class SendCustomerEmailCommandValidator : AbstractValidator<SendCustomerEmailCommand>
{
    public SendCustomerEmailCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        PersonEmailInputValidation.Apply(this);
    }
}

/// <summary>Validates <see cref="SendLeadEmailCommand"/>.</summary>
public sealed class SendLeadEmailCommandValidator : AbstractValidator<SendLeadEmailCommand>
{
    public SendLeadEmailCommandValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        PersonEmailInputValidation.Apply(this);
    }
}

/// <summary>
/// Shape of the content (rule 6): subject up to 200, body up to 50 000, known attachment kinds. The format and the
/// number of copies, the destination and the number and size of the attachments answer their own codes in
/// <see cref="PersonEmailSender"/>.
/// </summary>
internal static class PersonEmailInputValidation
{
    internal static void Apply<T>(AbstractValidator<T> validator) where T : IPersonEmailInput
    {
        validator.RuleFor(x => x.Subject).NotEmpty().MaximumLength(PersonEmailMessage.MaxSubjectLength);
        validator.RuleFor(x => x.BodyHtml).NotEmpty().MaximumLength(PersonEmailMessage.MaxBodyLength);
        validator.RuleFor(x => x.ToAddress).MaximumLength(PersonEmailMessage.MaxToAddressLength);
        validator.RuleFor(x => x.Cc).NotNull();
        validator.RuleFor(x => x.Attachments).NotNull();
        validator.RuleForEach(x => x.Attachments).ChildRules(a =>
        {
            a.RuleFor(x => x.SourceKind)
                .Must(kind => kind is (int)PersonEmailAttachmentSourceKind.ProductDocument or (int)PersonEmailAttachmentSourceKind.LeadDocument)
                .WithMessage("SourceKind must be 1 (ProductDocument) or 2 (LeadDocument).");
            a.RuleFor(x => x.SourceId).NotEmpty();
        });
    }
}
