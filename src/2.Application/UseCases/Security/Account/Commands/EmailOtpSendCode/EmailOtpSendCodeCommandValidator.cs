using FluentValidation;

namespace JOIN.Application.UseCases.Security.Account.Commands.EmailOtpSendCode;

/// <summary>
/// Reserved for future request constraints. Empty-body command today.
/// </summary>
public sealed class EmailOtpSendCodeCommandValidator : AbstractValidator<EmailOtpSendCodeCommand>
{
    public EmailOtpSendCodeCommandValidator()
    {
    }
}
