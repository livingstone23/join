namespace JOIN.Application.Common.Security;

/// <summary>
/// Represents the configurable password hardening rules applied during user registration and password changes.
/// </summary>
public class PasswordPolicySettings
{
    /// <summary>
    /// Gets or sets the minimum allowed password length.
    /// </summary>
    public int MinimumLength { get; set; } = 8;

    /// <summary>
    /// Gets or sets a value indicating whether repetitive character patterns such as 'aaa' must be rejected.
    /// </summary>
    public bool RestrictRepetitiveChars { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether common keyboard or numeric sequences must be rejected.
    /// </summary>
    public bool RestrictCommonSequences { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether personal identifiers such as username or email can appear in the password.
    /// </summary>
    public bool RestrictUsernameInPassword { get; set; } = true;
}
