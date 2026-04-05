using JOIN.Application.Common.Security;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace JOIN.Infrastructure.Security.Validators;

/// <summary>
/// Applies hardened password rules on top of the standard ASP.NET Core Identity validation pipeline.
/// </summary>
/// <param name="options">The password policy settings loaded from application configuration.</param>
public class CustomPasswordValidator(IOptions<PasswordPolicySettings> options) : IPasswordValidator<ApplicationUser>
{
    private static readonly string[] CommonSequences =
    [
        "123456",
        "654321",
        "qwerty",
        "asdfgh",
        "zxcvbn",
        "abcdef",
        "password"
    ];

    private readonly PasswordPolicySettings _settings = options.Value;

    /// <summary>
    /// Validates the supplied password according to the hardened policy rules.
    /// </summary>
    /// <param name="manager">The user manager executing the validation.</param>
    /// <param name="user">The target user being created or updated.</param>
    /// <param name="password">The password to validate.</param>
    /// <returns>An <see cref="IdentityResult"/> describing whether the password is valid.</returns>
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        var errors = new List<IdentityError>();

        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordRequired",
                Description = "Password is required."
            });

            return Task.FromResult(IdentityResult.Failed(errors.ToArray()));
        }

        if (password.Length < _settings.MinimumLength)
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordTooShort",
                Description = $"Password must be at least {_settings.MinimumLength} characters long."
            });
        }

        if (_settings.RestrictUsernameInPassword)
        {
            var personalValues = new[]
            {
                user.UserName,
                user.Email,
                user.Email?.Split('@').FirstOrDefault(),
                user.FirstName,
                user.LastName
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

            if (personalValues.Any(value => password.Contains(value!, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordContainsPersonalData",
                    Description = "Password cannot contain your username, email, first name, or last name."
                });
            }
        }

        if (_settings.RestrictRepetitiveChars && HasThreeConsecutiveRepeatedCharacters(password))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordContainsRepeatedCharacters",
                Description = "Password cannot contain repetitive character sequences such as 'aaa' or '111'."
            });
        }

        if (_settings.RestrictCommonSequences && ContainsCommonSequence(password))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordContainsCommonSequence",
                Description = "Password cannot contain common keyboard or numeric sequences."
            });
        }

        return Task.FromResult(errors.Count == 0
            ? IdentityResult.Success
            : IdentityResult.Failed(errors.ToArray()));
    }

    /// <summary>
    /// Determines whether the password contains three or more consecutive repeated characters.
    /// </summary>
    /// <param name="password">The password to inspect.</param>
    /// <returns><c>true</c> when a repetitive pattern exists; otherwise, <c>false</c>.</returns>
    private static bool HasThreeConsecutiveRepeatedCharacters(string password)
    {
        for (var index = 2; index < password.Length; index++)
        {
            var current = char.ToUpperInvariant(password[index]);
            var previous = char.ToUpperInvariant(password[index - 1]);
            var beforePrevious = char.ToUpperInvariant(password[index - 2]);

            if (current == previous && previous == beforePrevious)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether the password contains a known weak sequence.
    /// </summary>
    /// <param name="password">The password to inspect.</param>
    /// <returns><c>true</c> when a common sequence exists; otherwise, <c>false</c>.</returns>
    private static bool ContainsCommonSequence(string password)
    {
        return CommonSequences.Any(sequence => password.Contains(sequence, StringComparison.OrdinalIgnoreCase));
    }
}
