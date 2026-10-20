using System.Net.Mail;
using FluentValidation.Validators;

namespace JOIN.Application.Services.Email;

/// <summary>
/// SPEC 72 — the format validation of an email address: <see cref="MailAddress"/> (syntax) and the
/// FluentValidation <see cref="EmailValidator"/> (the address must have a real domain shape). A
/// contact of the person whose value does not pass it is not a valid address and cannot be a
/// destination nor a copy.
/// </summary>
public static class EmailAddressValidator
{
    private static readonly EmailValidator Validator = new();

    /// <summary><c>true</c> when the address is a well-formed email address.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!MailAddress.TryCreate(trimmed, out _))
        {
            return false;
        }

        return Validator.IsValid(trimmed);
    }
}
