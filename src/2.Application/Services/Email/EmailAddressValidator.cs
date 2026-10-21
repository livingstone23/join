using System.Net.Mail;
using FluentValidation;

namespace JOIN.Application.Services.Email;

/// <summary>
/// SPEC 72 — the format validation of an email address (rule 1): <see cref="MailAddress"/> (syntax, a bare address
/// without display name) and the FluentValidation <c>EmailAddress</c> rule, plus a domain with a dot. A contact of the
/// person whose value does not pass it is not a valid address and cannot be a destination nor a copy.
/// </summary>
public static class EmailAddressValidator
{
    private static readonly InlineValidator<string> Validator = CreateValidator();

    /// <summary><c>true</c> when the address is a well-formed email address.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!MailAddress.TryCreate(trimmed, out var address)
            || !string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase)
            || !address.Host.Contains('.', StringComparison.Ordinal)
            || address.Host.StartsWith('.')
            || address.Host.EndsWith('.'))
        {
            return false;
        }

        return Validator.Validate(trimmed).IsValid;
    }

    private static InlineValidator<string> CreateValidator()
    {
        var validator = new InlineValidator<string>();
        validator.RuleFor(x => x).EmailAddress();
        return validator;
    }
}
